using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.App.Features.Todo;

internal sealed record MicrosoftTodoLink(Guid ClientId, string AccountId, string ListId);
internal sealed record MicrosoftTodoSnapshot(MicrosoftTodoLink Link, string Name, TodoDocument Document, DateTimeOffset? RefreshedAt = null);
internal sealed record MicrosoftTodoPreferences(Guid ClientId, string? AccountId, MicrosoftTodoSnapshot[] Snapshots, string[] SelectedLists, DateTimeOffset? LastSync, Guid? TargetGroup = null)
{
    public static MicrosoftTodoPreferences Empty(Guid clientId) => new(clientId, null, [], [], null);

    public MicrosoftTodoPreferences Refresh(Guid clientId, string accountId, MicrosoftTodoSnapshot[] snapshots, DateTimeOffset now)
    {
        return UpdateSnapshots(snapshots, now) with
        {
            ClientId = clientId,
            AccountId = accountId,
            SelectedLists = snapshots.Select(item => item.Link.ListId).ToArray(),
            LastSync = snapshots.Length > 0 ? now : null,
        };
    }

    public MicrosoftTodoPreferences UpdateSnapshots(MicrosoftTodoSnapshot[] snapshots, DateTimeOffset now)
    {
        if (snapshots.Length == 0) return this;
        var refreshed = snapshots.Select(item => item.Link).ToHashSet();
        return this with
        {
            Snapshots = [.. Snapshots.Where(item => !refreshed.Contains(item.Link)),
                .. snapshots.Select(item => item with { RefreshedAt = now })],
            LastSync = now,
        };
    }
}

internal sealed class MicrosoftTodoSettings(IAppSettingsStore store, IClock clock,
    UberDashboard.Application.Synchronization.ISyncStore? syncStore = null,
    UberDashboard.Application.Synchronization.ISyncCoordinator? coordinator = null)
{
    internal const string Key = "integration.microsoft-todo";
    public event EventHandler<MicrosoftTodoPreferences>? Saved;

    public async Task<MicrosoftTodoPreferences> LoadAsync(CancellationToken token)
    {
        var json = await store.GetAsync(Key, token);
        if (json is null) return MicrosoftTodoPreferences.Empty(MicrosoftTodoSession.DefaultClientId);
        return await Task.Run(() => JsonSerializer.Deserialize<MicrosoftTodoPreferences>(json)
            ?? MicrosoftTodoPreferences.Empty(MicrosoftTodoSession.DefaultClientId), token);
    }

    public async Task SaveAsync(MicrosoftTodoPreferences preferences, CancellationToken token)
    {
        var json = await Task.Run(() => JsonSerializer.Serialize(preferences), token);
        await store.SetAsync(Key, json, clock.UtcNow, token);
        if (syncStore is not null && coordinator is not null)
        {
            var selected = preferences.SelectedLists.Select(id => "lists/" + Uri.EscapeDataString(id) + "/tasks").ToHashSet(StringComparer.Ordinal);
            foreach (var binding in (await syncStore.ListAsync(token)).Where(b => b.Enabled &&
                b.Provider == UberDashboard.Core.Synchronization.SyncProviderKind.MicrosoftTodo &&
                (b.ClientId != preferences.ClientId.ToString("D") || b.AccountId != preferences.AccountId || !selected.Contains(b.ResourcePath))))
                await coordinator.SetEnabledAsync(binding.Id, false, token);
        }
        Saved?.Invoke(this, preferences);
    }

    public static TodoDocument CreateDocument(IEnumerable<MicrosoftTodoTask> tasks) => new()
    {
        Items = tasks.Where(task => !string.IsNullOrWhiteSpace(task.Title))
            .Select(task => new TodoItemState(task.Title, task.Status == "completed")).ToArray(),
    };
}
