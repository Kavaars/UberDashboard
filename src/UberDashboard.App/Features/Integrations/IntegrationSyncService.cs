using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Features.Integrations;

/// <summary>Account/container identity, not the title or panel identity, selects the durable document.</summary>
internal sealed class IntegrationSyncService(ISyncStore store, SyncCoordinator coordinator, SyncScheduler scheduler, IntegrationAccounts? accounts = null)
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
        if (!await store.SaveAsync(new(binding, FormatVersion: SyncContainers.Format(binding)), -1, token).ConfigureAwait(false))
            return await EnableAsync(provider, clientId, accountId, resourcePath, name, token).ConfigureAwait(false);
        scheduler.Request(binding.Id);
        return binding.Id;
    }

    public void Request(Guid id) => scheduler.Request(id);

    internal async Task<Guid> EnableCatalogAsync(SyncBinding source, CancellationToken token, bool forceConsent = false)
    {
        if (source.Provider == SyncProviderKind.GoogleCalendar)
        {
            if (accounts is null) throw new SyncAuthenticationException();
            await Task.Run(() => accounts.RequestCalendarManagementAsync(source, token, forceConsent), token).ConfigureAwait(false);
        }
        return await EnableAsync(source.Provider, source.ClientId, source.AccountId, SyncContainers.CatalogPath(source.Provider),
            UberDashboard.Core.Localization.UiText.Get(source.Provider == SyncProviderKind.MicrosoftTodo ? "SyncManageLists" : "SyncManageCalendars"), token).ConfigureAwait(false);
    }

    internal async Task<Guid> EnableContentsAsync(SyncBinding catalog, SyncEntity entity, CancellationToken token)
    {
        if (!SyncContainers.IsCatalog(catalog) || entity.RemoteId is null || entity.Baseline is null || entity.Local is null || entity.Conflicts is { Length: > 0 })
            throw new InvalidOperationException();
        if (catalog.Provider == SyncProviderKind.MicrosoftTodo &&
            (accounts is null || !await accounts.IsMicrosoftListSelectedAsync(catalog, entity.RemoteId, token).ConfigureAwait(false))) throw new SyncListSelectionRequiredException();
        var path = catalog.Provider == SyncProviderKind.MicrosoftTodo ? "lists/" + Uri.EscapeDataString(entity.RemoteId) + "/tasks"
            : "calendars/" + Uri.EscapeDataString(entity.RemoteId) + "/events";
        return await EnableAsync(catalog.Provider, catalog.ClientId, catalog.AccountId, path,
            SyncContainerDraft.Read(catalog.Provider, entity.Local).Name, token).ConfigureAwait(false);
    }

    internal async Task<Guid> EnableAccessAsync(SyncBinding source, SyncEntity? calendar, bool sharing, CancellationToken token, bool forceConsent = false)
    {
        if (source.Provider != SyncProviderKind.GoogleCalendar || accounts is null) throw new SyncAuthenticationException();
        if (sharing && (calendar?.RemoteId is null || calendar.Baseline is null || calendar.Local is null || calendar.Conflicts is { Length: > 0 } ||
            calendar.Metadata?["accessRole"]?.GetValue<string>() != "owner")) throw new SyncContainerAccessException();
        var path = sharing ? "calendars/" + Uri.EscapeDataString(calendar!.RemoteId!) + "/acl" : GoogleCalendarAccess.Subscriptions;
        var target = source with { ResourcePath = path };
        await Task.Run(() => accounts.RequestGoogleAccessAsync(target, token, forceConsent), token).ConfigureAwait(false);
        return await EnableAsync(source.Provider, source.ClientId, source.AccountId, path,
            UberDashboard.Core.Localization.UiText.Get(sharing ? "SyncGoogleSharing" : "SyncGoogleSubscriptions") +
            (sharing ? " · " + (calendar!.Local?["summary"] ?? calendar.Metadata?["summary"])?.GetValue<string>() : ""), token).ConfigureAwait(false);
    }

    internal async Task<bool> RenewGoogleAsync(SyncBinding binding, CancellationToken token)
    {
        if (accounts is null) throw new SyncAuthenticationException();
        await Task.Run(() => accounts.RequestGoogleAccessAsync(binding, token, true), token).ConfigureAwait(false); return true;
    }
}

internal sealed class SyncListSelectionRequiredException : Exception;
