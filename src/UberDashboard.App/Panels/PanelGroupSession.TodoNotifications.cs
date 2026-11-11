using System.ComponentModel;
using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Features.Todo;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Panels;

public sealed partial class PanelGroupSession
{
    private MicrosoftTodoSettings? _todoSettings;
    private IAppSettingsStore? _todoSeenStore;
    private readonly SemaphoreSlim _todoGate = new(1, 1);
    private readonly Dictionary<PanelInstanceId, HashSet<string>> _todoSeen = [];
    private readonly HashSet<PanelInstanceId> _todoUnread = [];
    private readonly HashSet<object> _visibleTodoCards = [];
    internal void SetTodoVisibility(object card, bool visible)
    {
        var changed = visible ? _visibleTodoCards.Add(card) : _visibleTodoCards.Remove(card);
        if (changed && visible && _todoListening && _todoPreferences is not null) _ = UpdateTodoNotificationsAsync();
    }
    private MicrosoftTodoListViewModel? _todoView;
    private MicrosoftTodoPreferences? _todoPreferences;
    private bool _todoListening;

    internal bool HasNewTodoItems(PanelInstanceId id) => _todoUnread.Contains(id);
    internal static bool HasUnseenTodoItems(IEnumerable<string> current, IReadOnlySet<string> seen) => current.Any(id => !seen.Contains(id));

    internal void ConfigureTodoNotifications(MicrosoftTodoSettings settings, IAppSettingsStore store)
    {
        _todoSettings = settings;
        _todoSeenStore = store;
    }

    private async Task StartTodoNotificationsAsync(CancellationToken token)
    {
        if (!ImportsTasks || _todoSettings is null || _todoSeenStore is null || _todoListening) return;
        _todoListening = true;
        _todoSettings.Saved += TodoSettingsSaved;
        AttachTodoView();
        var loaded = await _todoSettings.LoadAsync(token);
        _todoPreferences ??= loaded;
        await RefreshTodoNotificationsAsync(token);
    }

    private void StopTodoNotifications()
    {
        _todoListening = false;
        if (_todoSettings is not null) _todoSettings.Saved -= TodoSettingsSaved;
        if (_todoView is not null) _todoView.PropertyChanged -= TodoViewChanged;
        _todoView = null;
        _todoPreferences = null;
    }

    private void AttachTodoView()
    {
        if (_todoView is not null) _todoView.PropertyChanged -= TodoViewChanged;
        _todoView = _todoListening ? ActiveViewModel as MicrosoftTodoListViewModel : null;
        if (_todoView is not null) _todoView.PropertyChanged += TodoViewChanged;
        if (_todoListening && _todoPreferences is not null) _ = UpdateTodoNotificationsAsync();
    }

    private async void TodoSettingsSaved(object? sender, MicrosoftTodoPreferences preferences)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() => { _todoPreferences = preferences; return UpdateTodoNotificationsAsync(); }).Task.Unwrap();
            return;
        }
        _todoPreferences = preferences;
        await UpdateTodoNotificationsAsync();
    }

    private void TodoViewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MicrosoftTodoListViewModel.Items) && _todoPreferences is not null)
            _ = UpdateTodoNotificationsAsync();
    }

    private async Task UpdateTodoNotificationsAsync()
    {
        try { await RefreshTodoNotificationsAsync(CancellationToken.None); }
        catch (Exception error) { ReportError(error); }
    }

    internal async Task RefreshTodoNotificationsAsync(CancellationToken token)
    {
        await _todoGate.WaitAsync(token);
        try
        {
            if (!_todoListening || _todoSeenStore is null || _todoPreferences is not { } preferences) return;
            var changed = false;
            foreach (var tab in Group.Tabs)
            {
                if (!IsMicrosoftTodoTab(tab.InstanceId)) continue;
                var record = await _instances.GetAsync(tab.InstanceId, token);
                if (record?.ConfigJson is null) continue;
                var link = JsonSerializer.Deserialize<MicrosoftTodoLink>(record.ConfigJson);
                var snapshot = preferences.Snapshots.FirstOrDefault(item => item.Link == link);
                if (snapshot is null) continue;
                var current = snapshot.Document.Items.Select(item => item.RemoteId).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToArray();
                if (current.Length == 0 && snapshot.Document.Items.Count > 0) continue;
                var key = $"todo.seen.{tab.InstanceId.Value:N}";
                if (!_todoSeen.TryGetValue(tab.InstanceId, out var seen))
                {
                    var json = await _todoSeenStore.GetAsync(key, token);
                    seen = json is null ? current.ToHashSet(StringComparer.Ordinal) : (JsonSerializer.Deserialize<string[]>(json) ?? []).ToHashSet(StringComparer.Ordinal);
                    _todoSeen[tab.InstanceId] = seen;
                    if (json is null) await _todoSeenStore.SetAsync(key, JsonSerializer.Serialize(seen), _clock.UtcNow, token);
                }
                if (tab.InstanceId == Group.ActiveId && _todoView is not null && _visibleTodoCards.Count > 0)
                {
                    var displayed = _todoView.Items.Select(item => item.RemoteId).Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToArray();
                    if (displayed.Any(id => !seen.Contains(id)))
                    {
                        var updated = seen.Concat(displayed).ToHashSet(StringComparer.Ordinal);
                        await _todoSeenStore.SetAsync(key, JsonSerializer.Serialize(updated), _clock.UtcNow, token);
                        _todoSeen[tab.InstanceId] = seen = updated;
                    }
                }
                var unread = HasUnseenTodoItems(current, seen);
                changed |= unread ? _todoUnread.Add(tab.InstanceId) : _todoUnread.Remove(tab.InstanceId);
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { _todoGate.Release(); }
    }
}
