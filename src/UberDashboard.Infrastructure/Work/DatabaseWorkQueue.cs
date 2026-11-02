using UberDashboard.Application.Abstractions;

namespace UberDashboard.Infrastructure.Work;

/// <summary>
/// Kolejka pracy z jednym dedykowanym wątkiem poza wątkiem UI. Wykonuje zadania
/// pojedynczo i w kolejności zgłoszenia, co jest wymagane przez synchroniczne
/// wywołania <c>Microsoft.Data.Sqlite</c> — metoda <c>Async</c> nie przenosi pracy
/// w tło, więc serializacją zajmuje się ten worker.
/// </summary>
/// <remarks>
/// Kontrakt <see cref="EnqueueExclusiveAsync{T}"/> jest w tej implementacji równoważny
/// zwykłemu kolejkowaniu: pojedynczy worker gwarantuje, że zadanie uruchomi się po
/// całej wcześniej zakolejkowanej pracy, a zadania zgłoszone później czekają za nim.
/// </remarks>
public sealed class DatabaseWorkQueue : IWorkQueue
{
    private readonly Queue<WorkItem> _items = new();
    private readonly object _sync = new();
    private readonly Thread _worker;
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TaskCompletionSource _idle = NewSignal();
    private int _pending;
    private bool _stopping;

    /// <summary>Tworzy kolejkę i uruchamia dedykowany wątek roboczy.</summary>
    public DatabaseWorkQueue(string name = "UberDashboard.DatabaseWorkQueue")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
        };
        _worker.Start();
    }

    /// <inheritdoc />
    public int PendingCount
    {
        get
        {
            lock (_sync)
            {
                return _pending;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? PendingCountChanged;

    /// <inheritdoc />
    public Task<T> EnqueueAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken = default) =>
        Enqueue(work, cancellationToken);

    /// <inheritdoc />
    public Task EnqueueAsync(Action<CancellationToken> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return Enqueue<object?>(
            token =>
            {
                work(token);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> EnqueueExclusiveAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken = default) =>
        Enqueue(work, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeout);

        while (true)
        {
            Task idle;
            lock (_sync)
            {
                if (_pending == 0)
                {
                    return true;
                }

                idle = _idle.Task;
            }

            try
            {
                await idle.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    return _pending == 0;
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            Monitor.PulseAll(_sync);
        }

        try
        {
            await _completed.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Wątek roboczy nie zakończył się w limicie; jest w tle i nie blokuje procesu.
        }

        GC.SuppressFinalize(this);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task<T> Enqueue<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new WorkItem
        {
            Token = cancellationToken,
            Work = token => work(token),
            Complete = value => completion.TrySetResult(value is null ? default! : (T)value),
            Fail = exception => completion.TrySetException(exception),
            Cancel = token => completion.TrySetCanceled(token),
        };

        lock (_sync)
        {
            if (_stopping)
            {
                completion.TrySetException(new ObjectDisposedException(nameof(DatabaseWorkQueue)));
                return completion.Task;
            }

            if (_pending == 0)
            {
                _idle = NewSignal();
            }

            _items.Enqueue(item);
            _pending++;
            Monitor.Pulse(_sync);
        }

        RaisePendingCountChanged();
        return completion.Task;
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                WorkItem item;
                lock (_sync)
                {
                    while (_items.Count == 0 && !_stopping)
                    {
                        Monitor.Wait(_sync);
                    }

                    if (_items.Count == 0)
                    {
                        break;
                    }

                    item = _items.Dequeue();
                }

                Execute(item);
            }
        }
        catch (Exception)
        {
            // Wyjątek wątku roboczego nie może zakończyć procesu; niewykonane zadania pozostają nierozwiązane.
        }
        finally
        {
            _completed.TrySetResult();
        }
    }

    private void Execute(WorkItem item)
    {
        Exception? error = null;
        object? result = null;
        var canceled = false;
        var token = item.Token;

        try
        {
            if (token.IsCancellationRequested)
            {
                canceled = true;
            }
            else
            {
                result = item.Work(token);
            }
        }
        catch (OperationCanceledException ex)
        {
            canceled = true;
            token = ex.CancellationToken;
        }
        catch (Exception ex)
        {
            error = ex;
        }

        CompleteWork();

        if (canceled)
        {
            item.Cancel(token);
        }
        else if (error is not null)
        {
            item.Fail(error);
        }
        else
        {
            item.Complete(result);
        }
    }

    private void CompleteWork()
    {
        TaskCompletionSource? idle = null;
        lock (_sync)
        {
            _pending--;
            if (_pending == 0)
            {
                idle = _idle;
            }
        }

        idle?.TrySetResult();
        RaisePendingCountChanged();
    }

    private void RaisePendingCountChanged() => PendingCountChanged?.Invoke(this, EventArgs.Empty);

    private sealed class WorkItem
    {
        public required CancellationToken Token { get; init; }

        public required Func<CancellationToken, object?> Work { get; init; }

        public required Action<object?> Complete { get; init; }

        public required Action<Exception> Fail { get; init; }

        public required Action<CancellationToken> Cancel { get; init; }
    }
}
