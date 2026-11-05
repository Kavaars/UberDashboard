using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.Application.Synchronization;

/// <summary>One supervised worker; panels only request work and never own network timers.</summary>
public sealed class SyncScheduler(ISyncStore store, ISyncCoordinator coordinator, IClock clock, ILogger<SyncScheduler> logger)
{
    private readonly ConcurrentDictionary<Guid, byte> _requested = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _due = new();
    private int _running;

    public void Request(Guid bindingId) => _requested.TryAdd(bindingId, 0);

    public async Task RunAsync(CancellationToken token)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) throw new InvalidOperationException("The synchronization worker is already running.");
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                foreach (var binding in await store.ListAsync(token).ConfigureAwait(false))
                {
                    if (!binding.Enabled) continue;
                    var requested = _requested.TryRemove(binding.Id, out _);
                    if (!requested && _due.TryGetValue(binding.Id, out var due) && due > clock.UtcNow) continue;
                    var interval = TimeSpan.FromSeconds(binding.Visible ? 60 : 300);
                    var next = interval * (0.9 + Random.Shared.NextDouble() * 0.2);
                    try { await coordinator.SynchronizeAsync(binding.Id, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        logger.LogWarning("Synchronization failed for {BindingId} with {ErrorType}", binding.Id, error.GetType().Name);
                    }
                    _due[binding.Id] = clock.UtcNow + next;
                }
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }
}
