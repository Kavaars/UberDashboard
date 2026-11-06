using UberDashboard.App.Features.Todo;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Synchronization;
using UberDashboard.Core.Synchronization;

namespace UberDashboard.App.Tests.Settings;

public sealed class ContainerSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selecting_lists_keeps_the_current_catalog_but_disconnect_stops_all_account_bindings(bool disconnect)
    {
        var client = Guid.NewGuid();
        var catalog = new SyncBinding(Guid.NewGuid(), SyncProviderKind.MicrosoftTodo, client.ToString("D"), "account", "lists", "Catalog", true);
        var selected = catalog with { Id = Guid.NewGuid(), ResourcePath = "lists/selected/tasks" };
        var deselected = selected with { Id = Guid.NewGuid(), ResourcePath = "lists/deselected/tasks" };
        var other = catalog with { Id = Guid.NewGuid(), AccountId = "another" };
        var google = catalog with { Id = Guid.NewGuid(), Provider = SyncProviderKind.GoogleCalendar, ResourcePath = "calendars" };
        var coordinator = new Coordinator();
        var settings = new MicrosoftTodoSettings(new Settings(), new Clock(), new Store([catalog, selected, deselected, other, google]), coordinator);
        await settings.SaveAsync(MicrosoftTodoPreferences.Empty(client) with { AccountId = disconnect ? null : "account", SelectedLists = ["selected"] }, default);
        Assert.Equal(disconnect, coordinator.Paused.Contains(catalog.Id)); Assert.Equal(disconnect, coordinator.Paused.Contains(selected.Id));
        Assert.Contains(deselected.Id, coordinator.Paused); Assert.Contains(other.Id, coordinator.Paused); Assert.DoesNotContain(google.Id, coordinator.Paused);
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class Settings : IAppSettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string valueJson, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Store(IReadOnlyList<SyncBinding> bindings) : ISyncStore
    {
        public Task<IReadOnlyList<SyncBinding>> ListAsync(CancellationToken token = default) => Task.FromResult(bindings);
        public Task<SyncState?> ReadAsync(Guid bindingId, CancellationToken token = default) => throw new InvalidOperationException();
        public Task<bool> SaveAsync(SyncState state, long expectedVersion, CancellationToken token = default) => throw new InvalidOperationException();
    }
    private sealed class Coordinator : ISyncCoordinator
    {
        public HashSet<Guid> Paused { get; } = [];
        public Task SetEnabledAsync(Guid bindingId, bool enabled, CancellationToken token = default) { Assert.False(enabled); Paused.Add(bindingId); return Task.CompletedTask; }
        public Task SynchronizeAsync(Guid bindingId, CancellationToken token = default) => throw new InvalidOperationException();
    }
}
