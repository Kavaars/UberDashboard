using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using System.IO;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.App.Features.Todo;

/// <summary>Manages delegated accounts in a Windows-protected token cache per profile.</summary>
internal sealed class MicrosoftTodoSession(IAppPaths paths)
{
    private static readonly string[] ReadScopes = ["https://graph.microsoft.com/Tasks.Read"];
    private static readonly string[] WriteScopes = ["https://graph.microsoft.com/Tasks.ReadWrite"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _client;
    private Guid _clientId;

    public static Guid DefaultClientId => Guid.TryParse(Environment.GetEnvironmentVariable("UBERDASHBOARD_MS_TODO_CLIENT_ID"), out var id)
        ? id : Guid.Parse("ae96d7dd-21ca-4fd7-adeb-242457b1d351");

    public async Task<AuthenticationResult> SignInAsync(Guid id, CancellationToken token, string? preferredAccountId, bool chooseAccount = false, bool allowWrite = false)
    {
        var scopes = allowWrite ? WriteScopes : ReadScopes;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var client = await GetClientAsync(id).ConfigureAwait(false);
            var accounts = (await client.GetAccountsAsync().ConfigureAwait(false)).ToArray();
            var account = preferredAccountId is not null
                ? accounts.FirstOrDefault(account => account.HomeAccountId.Identifier == preferredAccountId)
                : accounts.Length == 1 ? accounts[0] : null;
            if (account is not null && !chooseAccount)
            {
                try { return await client.AcquireTokenSilent(scopes, account).ExecuteAsync(token).ConfigureAwait(false); }
                catch (MsalUiRequiredException) { }
            }
            var request = client.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false);
            if (chooseAccount || (account is null && accounts.Length > 1)) request = request.WithPrompt(Prompt.SelectAccount);
            else if (account is not null) request = request.WithAccount(account);
            return await request.ExecuteAsync(token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<AuthenticationResult?> TrySignInSilentlyAsync(Guid id, string accountId, CancellationToken token, bool allowWrite = false)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var client = await GetClientAsync(id).ConfigureAwait(false);
            var account = (await client.GetAccountsAsync().ConfigureAwait(false))
                .FirstOrDefault(account => account.HomeAccountId.Identifier == accountId);
            if (account is null) return null;
            try
            {
                var signedIn = await client.AcquireTokenSilent(allowWrite ? WriteScopes : ReadScopes, account).ExecuteAsync(token).ConfigureAwait(false);
                var required = allowWrite ? "Tasks.ReadWrite" : "Tasks.Read";
                return signedIn.Account.HomeAccountId.Identifier == accountId && signedIn.Scopes.Any(scope =>
                    scope.Equals(required, StringComparison.OrdinalIgnoreCase) || scope.EndsWith("/" + required, StringComparison.OrdinalIgnoreCase) ||
                    !allowWrite && scope.EndsWith("Tasks.ReadWrite", StringComparison.OrdinalIgnoreCase)) ? signedIn : null;
            }
            catch (MsalUiRequiredException) { return null; }
        }
        finally { _gate.Release(); }
    }

    public async Task SignOutAsync(Guid id, string accountId, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var client = await GetClientAsync(id).ConfigureAwait(false);
            var account = (await client.GetAccountsAsync().ConfigureAwait(false))
                .FirstOrDefault(account => account.HomeAccountId.Identifier == accountId);
            if (account is not null) await client.RemoveAsync(account).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<IPublicClientApplication> GetClientAsync(Guid id)
    {
        if (_client is not null && _clientId == id) return _client;
        var client = PublicClientApplicationBuilder.Create(id.ToString("D"))
            .WithAuthority("https://login.microsoftonline.com/common")
            .WithRedirectUri("http://localhost")
            .Build();
        var folder = Path.Combine(paths.ProfileRoot, "Credentials");
        Directory.CreateDirectory(folder);
        var properties = new StorageCreationPropertiesBuilder($"ms-todo-{id:N}.cache", folder).Build();
        var cache = await MsalCacheHelper.CreateAsync(properties).ConfigureAwait(false);
        cache.VerifyPersistence();
        cache.RegisterCache(client.UserTokenCache);
        _clientId = id;
        return _client = client;
    }
}
