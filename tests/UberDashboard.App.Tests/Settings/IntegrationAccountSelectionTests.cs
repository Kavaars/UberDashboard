using System.IO;
using System.Text.Json;
using UberDashboard.App.Features.Integrations;
using UberDashboard.App.Features.Todo;
using UberDashboard.Application.Abstractions;
using UberDashboard.Core.Synchronization;
using UberDashboard.Infrastructure.Paths;
using UberDashboard.Infrastructure.Time;

namespace UberDashboard.App.Tests.Settings;

public sealed class IntegrationAccountSelectionTests
{
    [Theory]
    [InlineData(null, "list", "lists/list/tasks")]
    [InlineData("another-account", "list", "lists/list/tasks")]
    [InlineData("account", "deselected", "lists/list/tasks")]
    [InlineData(null, "list", "lists")]
    [InlineData("another-account", "list", "lists")]
    public async Task Disconnect_or_deselect_prevents_token_acquisition_even_if_an_old_binding_remains_enabled(string? account, string list, string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "UberDashboard.Tests", Guid.NewGuid().ToString("N"));
        var client = Guid.NewGuid();
        var preferences = MicrosoftTodoPreferences.Empty(client) with { AccountId = account, SelectedLists = [list] };
        var store = new Settings(JsonSerializer.Serialize(preferences));
        var paths = new AppPaths(root);
        var service = new IntegrationAccounts(new MicrosoftTodoSession(paths), store, paths, SystemClock.Instance);
        var binding = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, client.ToString("D"), "account", path, "Test", true);
        Assert.Null(await service.GetAccessTokenAsync(binding, true, default));
        Assert.False(Directory.Exists(root));
    }

    private sealed class Settings(string json) : IAppSettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(json);
        public Task SetAsync(string key, string valueJson, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
