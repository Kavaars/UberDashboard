using System.Collections.Concurrent;
using UberDashboard.Infrastructure.Work;

namespace UberDashboard.Infrastructure.Tests.Work;

public sealed class DatabaseWorkQueueTests
{
    [Fact]
    public async Task Work_runs_in_submission_order()
    {
        await using var queue = new DatabaseWorkQueue();
        var order = new ConcurrentQueue<int>();

        var tasks = Enumerable.Range(1, 16)
            .Select(i => queue.EnqueueAsync(_ => order.Enqueue(i)))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(1, 16), order.ToArray());
    }

    [Fact]
    public async Task Returns_work_result()
    {
        await using var queue = new DatabaseWorkQueue();

        var value = await queue.EnqueueAsync(_ => 6 * 7);

        Assert.Equal(42, value);
    }

    [Fact]
    public async Task Propagates_exception_to_caller()
    {
        await using var queue = new DatabaseWorkQueue();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => queue.EnqueueAsync<int>(_ => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task Pending_count_reaches_zero_when_idle()
    {
        await using var queue = new DatabaseWorkQueue();
        using var gate = new ManualResetEventSlim(false);

        var task = queue.EnqueueAsync(_ => gate.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, queue.PendingCount);

        gate.Set();
        await task;

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task Exclusive_work_runs_after_previously_queued_work()
    {
        await using var queue = new DatabaseWorkQueue();
        var order = new ConcurrentQueue<string>();
        using var gate = new ManualResetEventSlim(false);

        var first = queue.EnqueueAsync(_ =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            order.Enqueue("first");
        });
        var exclusive = queue.EnqueueExclusiveAsync(_ =>
        {
            order.Enqueue("exclusive");
            return true;
        });

        gate.Set();
        await Task.WhenAll(first, exclusive);

        Assert.Equal(new[] { "first", "exclusive" }, order.ToArray());
    }

    [Fact]
    public async Task Enqueue_with_cancelled_token_returns_cancelled_task()
    {
        await using var queue = new DatabaseWorkQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = queue.EnqueueAsync(_ => 1, cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Drain_returns_true_when_queue_is_empty()
    {
        await using var queue = new DatabaseWorkQueue();

        var drained = await queue.DrainAsync(TimeSpan.FromSeconds(2));

        Assert.True(drained);
    }

    [Fact]
    public async Task Pending_count_changed_event_is_raised()
    {
        await using var queue = new DatabaseWorkQueue();
        var raised = 0;
        queue.PendingCountChanged += (_, _) => Interlocked.Increment(ref raised);

        await queue.EnqueueAsync(_ => 1);

        Assert.True(raised >= 2);
    }
}
