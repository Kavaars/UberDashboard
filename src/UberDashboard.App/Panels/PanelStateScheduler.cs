using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Panels;

/// <summary>
/// Harmonogram autozapisu stanu paneli. Każda zmiana zgłoszona przez instancję
/// restartuje krótki licznik; zapis wykonuje się dopiero po ustaniu zmian i zawsze
/// poza wątkiem interfejsu. Dzięki temu szybkie pisanie w notatniku generuje jeden
/// zapis zamiast serii transakcji bazy.
/// </summary>
public sealed class PanelStateScheduler : IAsyncDisposable
{
    /// <summary>Opóźnienie, po którym zmiana zostaje utrwalona.</summary>
    public static TimeSpan DebounceDelay { get; } = TimeSpan.FromMilliseconds(600);

    private readonly ConcurrentDictionary<PanelInstanceId, Entry> _entries = new();
    private readonly ILogger<PanelStateScheduler> _logger;
    private bool _disposed;

    /// <summary>Tworzy harmonogram autozapisu.</summary>
    public PanelStateScheduler(ILogger<PanelStateScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Liczba instancji objętych autozapisem.</summary>
    public int TrackedCount => _entries.Count;

    /// <summary>Rozpoczyna śledzenie instancji.</summary>
    public void Track(IPanelStateSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_disposed)
        {
            return;
        }

        _entries.AddOrUpdate(
            sink.InstanceId,
            _ => new Entry(sink),
            (_, existing) =>
            {
                CancelPending(existing);
                return new Entry(sink);
            });
    }

    /// <summary>Kończy śledzenie instancji.</summary>
    public bool Untrack(PanelInstanceId instanceId)
    {
        if (!_entries.TryRemove(instanceId, out var entry))
        {
            return false;
        }

        CancelPending(entry);
        return true;
    }

    /// <summary>Zgłasza zmianę stanu; zapis nastąpi po ustaniu kolejnych zmian.</summary>
    public void RequestSave(PanelInstanceId instanceId)
    {
        if (_disposed || !_entries.TryGetValue(instanceId, out var entry))
        {
            return;
        }

        CancellationTokenSource pending;
        lock (entry.Gate)
        {
            entry.Pending?.Cancel();
            pending = new CancellationTokenSource();
            entry.Pending = pending;
        }

        _ = RunDebouncedAsync(entry, pending);
    }

    /// <summary>Utrwala stan instancji natychmiast, pomijając opóźnienie.</summary>
    public async Task FlushAsync(PanelInstanceId instanceId, CancellationToken cancellationToken)
    {
        if (!_entries.TryGetValue(instanceId, out var entry))
        {
            return;
        }

        lock (entry.Gate)
        {
            entry.Pending?.Cancel();
            entry.Pending = null;
        }

        await PersistAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Utrwala stan wszystkich śledzonych instancji.</summary>
    public async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in _entries.Values)
        {
            lock (entry.Gate)
            {
                entry.Pending?.Cancel();
                entry.Pending = null;
            }
        }

        foreach (var entry in _entries.Values)
        {
            await PersistAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await FlushAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się opróżnić harmonogramu autozapisu przy zamykaniu.");
        }

        _disposed = true;

        foreach (var entry in _entries.Values)
        {
            CancelPending(entry);
        }

        _entries.Clear();
    }

    private async Task RunDebouncedAsync(Entry entry, CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(DebounceDelay, pending.Token).ConfigureAwait(false);
            await PersistAsync(entry, pending.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Zmiana została nadpisana przez kolejną — zapis wykonamy później.
        }
        finally
        {
            lock (entry.Gate)
            {
                if (ReferenceEquals(entry.Pending, pending))
                {
                    entry.Pending = null;
                }
            }

            pending.Dispose();
        }
    }

    private async Task PersistAsync(Entry entry, CancellationToken cancellationToken)
    {
        try
        {
            await entry.Sink.PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Anulowanie nie jest błędem.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Autozapis stanu panelu {InstanceId} nie powiódł się.", entry.Sink.InstanceId);
        }
    }

    private static void CancelPending(Entry entry)
    {
        lock (entry.Gate)
        {
            entry.Pending?.Cancel();
            entry.Pending = null;
        }
    }

    private sealed class Entry(IPanelStateSink sink)
    {
        public object Gate { get; } = new();

        public IPanelStateSink Sink { get; } = sink;

        public CancellationTokenSource? Pending { get; set; }
    }
}
