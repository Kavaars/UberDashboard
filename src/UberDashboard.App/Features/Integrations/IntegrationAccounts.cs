using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.App.Features.Calendar;
using UberDashboard.App.Features.Todo;
using UberDashboard.App.Hosting;
using UberDashboard.App.Panels;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Features.Integrations;

/// <summary>Only credential references are stored in settings; bearer/refresh tokens stay in protected stores.</summary>
internal sealed class IntegrationAccounts(MicrosoftTodoSession microsoft, IAppSettingsStore settings, IAppPaths paths, IClock clock,
    IIntegrationDiagnostics? diagnostics = null, IPanelInstanceStore? instances = null)
    : IIntegrationAccountService, IIntegrationFileAccountService
{
    private const string Key = "integration.google.sessions";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RegisterGoogleAsync(PanelInstanceId instanceId, GoogleCalendarClient.GoogleConnection connection, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!await IsCalendarAsync(instanceId, token).ConfigureAwait(false))
                throw new InvalidOperationException("The calendar panel no longer exists.");
            var references = await ReadReferencesAsync(token).ConfigureAwait(false);
            var updated = references.Where(r => r.InstanceId != instanceId.Value)
                .Append(new GoogleReference(instanceId.Value, connection.ClientId, connection.AccountId)).ToArray();
            await settings.SetAsync(Key, JsonSerializer.Serialize(updated), clock.UtcNow, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    internal async Task RemoveGoogleSessionsAsync(IReadOnlyCollection<PanelInstanceId> ids, CancellationToken token)
    {
        if (ids.Count == 0) return;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            foreach (var id in ids)
                await GoogleClient(id).DisconnectAsync(token).ConfigureAwait(false);

            var removed = ids.Select(id => id.Value).ToHashSet();
            var references = await ReadReferencesAsync(token).ConfigureAwait(false);
            var remaining = references.Where(reference => !removed.Contains(reference.InstanceId)).ToArray();
            if (remaining.Length != references.Length)
                await settings.SetAsync(Key, JsonSerializer.Serialize(remaining), clock.UtcNow, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token) => AccessAsync(binding, write, false, token);
    public Task<string?> GetFileAccessTokenAsync(SyncBinding binding, CancellationToken token) => AccessAsync(binding, true, true, token);
    private async Task<string?> AccessAsync(SyncBinding binding, bool write, bool files, CancellationToken token)
    {
        if (binding.Provider == SyncProviderKind.MicrosoftTodo)
        {
            if (!Guid.TryParse(binding.ClientId, out var clientId)) return null;
            var selectedJson = await settings.GetAsync(MicrosoftTodoSettings.Key, token).ConfigureAwait(false);
            var selected = selectedJson is null ? null : JsonSerializer.Deserialize<MicrosoftTodoPreferences>(selectedJson);
            if (selected is null || selected.ClientId != clientId || selected.AccountId != binding.AccountId ||
                binding.ResourcePath != "lists" && !selected.SelectedLists.Any(id => MicrosoftTodoRelations.TaskCollectionPath(binding.ResourcePath) == "lists/" + Uri.EscapeDataString(id) + "/tasks")) return null;
            try { return (await microsoft.TrySignInSilentlyAsync(clientId, binding.AccountId, token, write).ConfigureAwait(false))?.AccessToken; }
            catch (Microsoft.Identity.Client.MsalException error) { diagnostics?.Report("MicrosoftTodo", "oauth", error); throw new SyncAuthenticationException(); }
        }
        foreach (var reference in (await ReadReferencesAsync(token).ConfigureAwait(false))
            .Where(r => r.ClientId == binding.ClientId && r.AccountId == binding.AccountId))
        {
            var id = new PanelInstanceId(reference.InstanceId);
            if (!await IsCalendarAsync(id, token).ConfigureAwait(false)) continue;
            var client = GoogleClient(id);
            string? access;
            try { access = await client.GetAccessTokenAsync(binding.ClientId, binding.AccountId, write, token,
                files ? [GoogleCalendarClient.FileScope] : GoogleCalendarClient.RequiredScopes(binding, write)).ConfigureAwait(false); }
            catch (Exception error) when (error is Google.Apis.Auth.OAuth2.Responses.TokenResponseException or System.Security.Cryptography.CryptographicException)
            { diagnostics?.Report("GoogleCalendar", "oauth", error); throw new SyncAuthenticationException(); }
            if (access is not null && await IsCalendarAsync(id, token).ConfigureAwait(false)) return access;
        }
        return null;
    }

    internal async Task RequestCalendarManagementAsync(SyncBinding binding, CancellationToken token, bool forceConsent = false)
        => await RequestGoogleAccessAsync(binding with { ResourcePath = "calendars" }, token, forceConsent).ConfigureAwait(false);

    internal async Task RequestGoogleAccessAsync(SyncBinding binding, CancellationToken token, bool forceConsent = false, bool files = false)
    {
        foreach (var reference in (await ReadReferencesAsync(token).ConfigureAwait(false)).Where(r => r.ClientId == binding.ClientId && r.AccountId == binding.AccountId))
        {
            var id = new PanelInstanceId(reference.InstanceId);
            if (!await IsCalendarAsync(id, token).ConfigureAwait(false)) continue;
            var client = GoogleClient(id); var connection = await client.GetConnectionAsync(token).ConfigureAwait(false);
            if (connection?.ClientId != binding.ClientId || connection.AccountId != binding.AccountId) continue;
            if (files) await client.RequestFileAccessAsync(binding.ClientId, binding.AccountId, token, forceConsent).ConfigureAwait(false);
            else await client.RequestAccessAsync(binding, token, forceConsent).ConfigureAwait(false);
            return;
        }
        throw new SyncAuthenticationException();
    }

    internal async Task<bool> IsMicrosoftListSelectedAsync(SyncBinding binding, string id, CancellationToken token)
    {
        var json = await settings.GetAsync(MicrosoftTodoSettings.Key, token).ConfigureAwait(false);
        var selected = json is null ? null : JsonSerializer.Deserialize<MicrosoftTodoPreferences>(json);
        return selected?.ClientId.ToString("D") == binding.ClientId && selected.AccountId == binding.AccountId && selected.SelectedLists.Contains(id, StringComparer.Ordinal);
    }

    private async Task<GoogleReference[]> ReadReferencesAsync(CancellationToken token)
    {
        var json = await settings.GetAsync(Key, token).ConfigureAwait(false);
        return json is null ? [] : JsonSerializer.Deserialize<GoogleReference[]>(json) ?? [];
    }

    private GoogleCalendarClient GoogleClient(PanelInstanceId id) =>
        new(new PanelStorage(paths.GetPluginInstanceDirectory(AppServiceCollectionExtensions.BuiltInPluginId, id)));

    private async Task<bool> IsCalendarAsync(PanelInstanceId id, CancellationToken token) =>
        instances is not null && await instances.GetAsync(id, token).ConfigureAwait(false) is { } record &&
        record.PluginId == AppServiceCollectionExtensions.BuiltInPluginId && record.PanelTypeId == CalendarPanelDescriptor.Id;

    private sealed record GoogleReference(Guid InstanceId, string ClientId, string AccountId);
}
