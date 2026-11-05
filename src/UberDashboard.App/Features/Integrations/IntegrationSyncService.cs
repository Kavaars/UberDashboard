using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Features.Integrations;

/// <summary>Account/container identity, not the title or panel identity, selects the durable document.</summary>
internal sealed class IntegrationSyncService(ISyncStore store, SyncCoordinator coordinator, SyncScheduler scheduler)
{
    public ISyncStore Store => store;
    public SyncCoordinator Coordinator => coordinator;

    public async Task<Guid> EnableAsync(SyncProviderKind provider, string clientId, string accountId, string resourcePath,
        string name, CancellationToken token)
    {
        var existing = (await store.ListAsync(token).ConfigureAwait(false)).FirstOrDefault(b => b.Provider == provider &&
            b.ClientId == clientId && b.AccountId == accountId && b.ResourcePath == resourcePath);
        if (existing is not null)
        {
            await coordinator.SetEnabledAsync(existing.Id, true, token).ConfigureAwait(false);
            scheduler.Request(existing.Id);
            return existing.Id;
        }
        var binding = new SyncBinding(Guid.NewGuid(), provider, clientId, accountId, resourcePath, name, true, true);
        if (!await store.SaveAsync(new(binding), -1, token).ConfigureAwait(false))
            return await EnableAsync(provider, clientId, accountId, resourcePath, name, token).ConfigureAwait(false);
        scheduler.Request(binding.Id);
        return binding.Id;
    }

    public void Request(Guid id) => scheduler.Request(id);
}
