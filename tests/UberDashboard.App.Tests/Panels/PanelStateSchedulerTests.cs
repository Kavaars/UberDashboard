using Microsoft.Extensions.Logging.Abstractions;
using UberDashboard.App.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Tests.Panels;

public sealed class PanelStateSchedulerTests
{
    [Fact]
    public async Task Explicit_flush_propagates_failure_instead_of_confirming_save()
    {
        await using var scheduler = new PanelStateScheduler(NullLogger<PanelStateScheduler>.Instance);
        var sink = new FailingSink();
        scheduler.Track(sink);
        await Assert.ThrowsAsync<IOException>(() => scheduler.FlushAsync(sink.InstanceId, CancellationToken.None));
    }
    private sealed class FailingSink : IPanelStateSink
    {
        public PanelInstanceId InstanceId { get; } = PanelInstanceId.New();
        public Task PersistAsync(CancellationToken cancellationToken) => Task.FromException(new IOException("Testowy błąd zapisu"));
    }
}
