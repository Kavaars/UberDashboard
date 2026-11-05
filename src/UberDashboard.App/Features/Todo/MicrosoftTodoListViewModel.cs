using UberDashboard.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Features.Integrations;
using UberDashboard.Core.Synchronization;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Features.Todo;

public sealed class MicrosoftTodoListViewModel : ObservableObject
{
    private MicrosoftTodoSnapshot? _snapshot;
    private string _status = "";
    private MicrosoftTodoLink? _link;
    private IntegrationSyncService? _sync;
    private MicrosoftTodoSession? _session;
    private IPanelNotifier? _notifier;
    private CancellationToken _closeToken;
    private IReadOnlyList<TodoItemState>? _localItems;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _reading;
    public IReadOnlyList<TodoItemState> Items => _localItems ?? _snapshot?.Document.Items ?? [];
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public IAsyncRelayCommand OpenSyncEditorCommand { get; }

    public MicrosoftTodoListViewModel()
    {
        OpenSyncEditorCommand = new AsyncRelayCommand(OpenSyncEditorAsync, () => _sync is not null);
        _timer.Tick += async (_, _) => await RefreshLocalAsync();
    }

    internal void Configure(IntegrationSyncService sync, MicrosoftTodoSession session, IPanelNotifier notifier, CancellationToken closeToken)
    {
        _sync = sync; _session = session; _notifier = notifier; _closeToken = closeToken;
        OpenSyncEditorCommand.NotifyCanExecuteChanged(); _timer.Start();
    }

    internal void Suspend() => _timer.Stop();
    internal void Resume() { if (_sync is not null) _timer.Start(); }

    private async Task<Guid?> FindBindingAsync()
    {
        if (_sync is null || _link is null) return null;
        var path = "lists/" + Uri.EscapeDataString(_link.ListId) + "/tasks";
        return (await _sync.Store.ListAsync(_closeToken)).FirstOrDefault(b => b.Provider == SyncProviderKind.MicrosoftTodo &&
            b.ClientId == _link.ClientId.ToString("D") && b.AccountId == _link.AccountId && b.ResourcePath == path)?.Id;
    }

    private async Task RefreshLocalAsync()
    {
        if (_reading || _closeToken.IsCancellationRequested) return;
        _reading = true;
        try
        {
            if (await FindBindingAsync() is not { } id || _sync is null) return;
            var state = await _sync.Store.ReadAsync(id, _closeToken);
            if (state is null) return;
            _localItems = state.Items.Where(e => e.Kind == UberDashboard.Core.Synchronization.SyncEntityKind.Item && e.Local is not null).Select(e => new TodoItemState(
                e.Local!["title"]?.GetValue<string>() ?? "", e.Local["status"]?.GetValue<string>() == "completed", e.LocalId)).ToArray();
            Status = UiText.Format("SyncQueueSummary", _localItems.Count, state.Queue.Count,
                state.Binding.Enabled ? UiText.Get("SyncEnabled") : UiText.Get("SyncPaused"));
            OnPropertyChanged(nameof(Items));
        }
        catch (OperationCanceledException) when (_closeToken.IsCancellationRequested) { }
        catch (Exception) { Status = UiText.Get("SyncLocalReadFailed"); }
        finally { _reading = false; }
    }

    private async Task OpenSyncEditorAsync()
    {
        if (_sync is null || _session is null || _link is null || System.Windows.Application.Current.MainWindow is not { } owner) return;
        try
        {
            var existing = await FindBindingAsync();
            if (existing is null)
            {
                if (UberDashboard.App.Controls.ThemedMessageBox.Show(owner, UiText.Get("SyncReadWriteConsentNotice"),
                    UiText.Get("SyncEditorTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;
                var credential = await _session.SignInAsync(_link.ClientId, _closeToken, _link.AccountId, allowWrite: true);
                if (credential.Account.HomeAccountId.Identifier != _link.AccountId ||
                    !credential.Scopes.Any(s => s.Equals("Tasks.ReadWrite", StringComparison.OrdinalIgnoreCase) || s.EndsWith("/Tasks.ReadWrite", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException();
                existing = await _sync.EnableAsync(SyncProviderKind.MicrosoftTodo, _link.ClientId.ToString("D"), _link.AccountId,
                    "lists/" + Uri.EscapeDataString(_link.ListId) + "/tasks", _snapshot?.Name ?? "Microsoft To Do", _closeToken);
            }
            SyncEditorWindow.Open(owner, _sync, existing.Value, _closeToken, async () =>
            {
                var credential = await _session.SignInAsync(_link.ClientId, _closeToken, _link.AccountId, allowWrite: true);
                return credential.Account.HomeAccountId.Identifier == _link.AccountId && credential.Scopes.Any(s =>
                    s.Equals("Tasks.ReadWrite", StringComparison.OrdinalIgnoreCase) || s.EndsWith("/Tasks.ReadWrite", StringComparison.OrdinalIgnoreCase));
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _notifier?.Publish(UiText.Get("SyncReconnectRequired"), PanelNotificationSeverity.Warning); }
    }

    internal void Load(MicrosoftTodoLink link, MicrosoftTodoPreferences preferences)
    {
        _link = link;
        _snapshot = preferences.Snapshots.FirstOrDefault(item => item.Link == link);
        OnPropertyChanged(nameof(Items));
        var enabled = preferences.ClientId == link.ClientId && preferences.AccountId == link.AccountId &&
            preferences.SelectedLists.Contains(link.ListId);
        Status = enabled
            ? UiText.Format("MicrosoftToDoReadOnlyLastRefreshed", (_snapshot?.RefreshedAt ?? preferences.LastSync)?.ToLocalTime())
            : UiText.Get("MicrosoftToDoLatestCopySyncIs");
    }
}
