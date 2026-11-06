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
internal sealed class IntegrationAccounts(MicrosoftTodoSession microsoft, IAppSettingsStore settings, IAppPaths paths, IClock clock)
    : IIntegrationAccountService
{
    private const string Key = "integration.google.sessions";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task RegisterGoogleAsync(PanelInstanceId instanceId, GoogleCalendarClient.GoogleConnection connection, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var references = await ReadReferencesAsync(token).ConfigureAwait(false);
            var updated = references.Where(r => r.InstanceId != instanceId.Value)
                .Append(new GoogleReference(instanceId.Value, connection.ClientId, connection.AccountId)).ToArray();
            await settings.SetAsync(Key, JsonSerializer.Serialize(updated), clock.UtcNow, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> GetAccessTokenAsync(SyncBinding binding, bool write, CancellationToken token)
    {
        if (binding.Provider == SyncProviderKind.MicrosoftTodo)
        {
            if (!Guid.TryParse(binding.ClientId, out var clientId)) return null;
            var selectedJson = await settings.GetAsync(MicrosoftTodoSettings.Key, token).ConfigureAwait(false);
            var selected = selectedJson is null ? null : JsonSerializer.Deserialize<MicrosoftTodoPreferences>(selectedJson);
            if (selected is null || selected.ClientId != clientId || selected.AccountId != binding.AccountId ||
                binding.ResourcePath != "lists" && !selected.SelectedLists.Any(id => MicrosoftTodoRelations.TaskCollectionPath(binding.ResourcePath) == "lists/" + Uri.EscapeDataString(id) + "/tasks")) return null;
            try { return (await microsoft.TrySignInSilentlyAsync(clientId, binding.AccountId, token, write).ConfigureAwait(false))?.AccessToken; }
            catch (Microsoft.Identity.Client.MsalException) { throw new SyncAuthenticationException(); }
        }
        foreach (var reference in (await ReadReferencesAsync(token).ConfigureAwait(false))
            .Where(r => r.ClientId == binding.ClientId && r.AccountId == binding.AccountId))
        {
            var storage = new PanelStorage(paths.GetPluginInstanceDirectory(AppServiceCollectionExtensions.BuiltInPluginId,
                new PanelInstanceId(reference.InstanceId)));
            var client = new GoogleCalendarClient(storage);
            string? access;
            try { access = await client.GetAccessTokenAsync(binding.ClientId, binding.AccountId, write, token,
                SyncContainers.IsCatalog(binding) ? GoogleCalendarClient.CalendarManagementScopes : null).ConfigureAwait(false); }
            catch (Exception error) when (error is Google.Apis.Auth.OAuth2.Responses.TokenResponseException or System.Security.Cryptography.CryptographicException)
            { throw new SyncAuthenticationException(); }
            if (access is not null) return access;
        }
        return null;
    }

    internal async Task RequestCalendarManagementAsync(SyncBinding binding, CancellationToken token, bool forceConsent = false)
    {
        foreach (var reference in (await ReadReferencesAsync(token).ConfigureAwait(false)).Where(r => r.ClientId == binding.ClientId && r.AccountId == binding.AccountId))
        {
            var storage = new PanelStorage(paths.GetPluginInstanceDirectory(AppServiceCollectionExtensions.BuiltInPluginId, new PanelInstanceId(reference.InstanceId)));
            var client = new GoogleCalendarClient(storage); var connection = await client.GetConnectionAsync(token).ConfigureAwait(false);
            if (connection?.ClientId != binding.ClientId || connection.AccountId != binding.AccountId) continue;
            await client.RequestCalendarManagementAsync(binding.ClientId, binding.AccountId, token, forceConsent).ConfigureAwait(false); return;
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

    private sealed record GoogleReference(Guid InstanceId, string ClientId, string AccountId);
}
