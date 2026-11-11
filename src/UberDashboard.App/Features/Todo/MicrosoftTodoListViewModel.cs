using UberDashboard.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Features.Integrations;
using UberDashboard.Core.Synchronization;
using UberDashboard.Contracts.Host;
using UberDashboard.App.Panels;
using System.Text.Json.Nodes;

namespace UberDashboard.App.Features.Todo;

public sealed class MicrosoftTodoListViewModel : ObservableObject, IPanelOptionsPresenter
{
    private MicrosoftTodoSnapshot? _snapshot;
    private string _status = "";
    private string _newItemText = "";
    private MicrosoftTodoLink? _link;
    private IntegrationSyncService? _sync;
    private MicrosoftTodoSession? _session;
    private IPanelNotifier? _notifier;
    private CancellationToken _closeToken;
    private IReadOnlyList<TodoItemState>? _localItems;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _reading;
    private bool _isOptionsOpen;
    private readonly TodoPanelViewModel _options;
    private SyncState? _state;
    private MicrosoftTodoRefresh? _refresh;
    private DateTimeOffset? _snapshotTime;
    public string LastSyncText => (_state is null ? _snapshotTime : _state.LastSuccess) is { } date ? UiText.Format("TodoLastSync", date.ToLocalTime().ToString("g")) : UiText.Get("TodoNeverSync");
    public IAsyncRelayCommand SyncNowCommand { get; }
    public bool HasOptions => true;
    public bool IsOptionsOpen { get => _isOptionsOpen; set => SetProperty(ref _isOptionsOpen, value); }
    public IRelayCommand ToggleOptionsCommand { get; }
    public IAsyncRelayCommand ClearCompletedCommand { get; }
    public IAsyncRelayCommand KeepCompletedCommand { get; }
    public int ItemStyle { get => _options.ItemStyle; set { _options.ItemStyle = value; OnPropertyChanged(); } }
    public IAsyncRelayCommand<TodoItemState> ToggleTaskCommand { get; }
    public IAsyncRelayCommand<TodoItemState> EditTaskCommand { get; }
    public double EdgeSpacing { get => _options.EdgeSpacing; set { _options.EdgeSpacing = value; OnPropertyChanged(); OnPropertyChanged(nameof(EdgePadding)); } }
    public double EntrySpacing { get => _options.EntrySpacing; set { _options.EntrySpacing = value; OnPropertyChanged(); OnPropertyChanged(nameof(EntryPadding)); } }
    public System.Windows.Thickness EdgePadding => _options.EdgePadding;
    public System.Windows.Thickness EntryPadding => _options.EntryPadding;
    public bool ShowOnlyIncomplete { get => _options.ShowOnlyIncomplete; set { _options.ShowOnlyIncomplete = value; OnPropertyChanged(); OnPropertyChanged(nameof(Items)); } }
    public int CompletedDisplayLimit { get => _options.CompletedDisplayLimit; set { _options.CompletedDisplayLimit = value; OnPropertyChanged(); OnPropertyChanged(nameof(Items)); } }
    public int CompletedKeepLimit { get => _options.CompletedKeepLimit; set { _options.CompletedKeepLimit = value; OnPropertyChanged(); } }
    public IReadOnlyList<TodoItemState> Items => SortItems(_localItems ?? _snapshot?.Document.Items ?? [], ShowOnlyIncomplete, CompletedDisplayLimit);
    internal static IReadOnlyList<TodoItemState> SortItems(IReadOnlyList<TodoItemState> source, bool onlyIncomplete, int limit)
    {
        var indexed = source.Select((item, index) => (item, index)).ToArray();
        return indexed.Where(x => !x.item.IsDone).OrderByDescending(x => x.item.CreatedAt).ThenByDescending(x => x.index).Select(x => x.item)
            .Concat(onlyIncomplete ? [] : indexed.Where(x => x.item.IsDone).OrderByDescending(x => x.item.CompletedAt).ThenByDescending(x => x.index).Take(limit).Select(x => x.item)).ToArray();
    }
    private static DateTimeOffset? Date(string? value) => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    private static TodoItemState ToItem(SyncEntity e) => new(e.Local!["title"]?.GetValue<string>() ?? "", e.Local["status"]?.GetValue<string>() == "completed", e.LocalId,
        Date(e.Local["completedDateTime"]?["dateTime"]?.GetValue<string>()), Date(e.Metadata?["createdDateTime"]?.GetValue<string>()), e.RemoteId);
    public string Status { get => _status; private set { if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(SyncStatusText)); } }
    public string SyncStatusText => string.IsNullOrWhiteSpace(Status) ? LastSyncText : $"{Status} · {LastSyncText}";
    public string NewItemText { get => _newItemText; set => SetProperty(ref _newItemText, value); }
    public IAsyncRelayCommand AddItemCommand { get; }
    public IAsyncRelayCommand OpenSyncEditorCommand { get; }

    internal MicrosoftTodoListViewModel(TodoPanelViewModel options)
    {
        _options = options;
        ToggleTaskCommand = new AsyncRelayCommand<TodoItemState>(ToggleTaskAsync);
        EditTaskCommand = new AsyncRelayCommand<TodoItemState>(EditTaskAsync);
        AddItemCommand = new AsyncRelayCommand(AddItemAsync);
        ToggleOptionsCommand = new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);
        ClearCompletedCommand = new AsyncRelayCommand(() => RemoveCompletedAsync(0));
        KeepCompletedCommand = new AsyncRelayCommand(() => RemoveCompletedAsync(CompletedKeepLimit));
        OpenSyncEditorCommand = new AsyncRelayCommand(OpenSyncEditorAsync, () => _sync is not null);
        SyncNowCommand = new AsyncRelayCommand(SyncNowAsync);
        _timer.Tick += async (_, _) => await RefreshLocalAsync();
    }

    internal void Configure(IntegrationSyncService sync, MicrosoftTodoSession session, IPanelNotifier notifier, CancellationToken closeToken, MicrosoftTodoRefresh? refresh = null)
    {
        _refresh = refresh; _sync = sync; _session = session; _notifier = notifier; _closeToken = closeToken;
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

    internal async Task DeleteRemoteListAsync(CancellationToken token)
    {
        if (_sync is null || _link is null) throw new InvalidOperationException(UiText.Get("TodoDeleteRemoteUnavailable"));
        var catalogId = await _sync.EnableAsync(SyncProviderKind.MicrosoftTodo, _link.ClientId.ToString("D"), _link.AccountId,
            SyncContainers.CatalogPath(SyncProviderKind.MicrosoftTodo), UiText.Get("SyncManageLists"), token);
        await _sync.Coordinator.SynchronizeAsync(catalogId, token);
        var state = await _sync.Store.ReadAsync(catalogId, token) ?? throw new InvalidOperationException(UiText.Get("TodoDeleteRemoteUnavailable"));
        var entity = state.Items.SingleOrDefault(item => item.RemoteId == _link.ListId && item.Local is not null);
        if (entity is null || entity.Conflicts is { Length: > 0 } || !SyncContainers.CanDelete(SyncProviderKind.MicrosoftTodo, entity.Metadata))
            throw new InvalidOperationException(UiText.Get("TodoDeleteRemoteUnavailable"));
        await _sync.Coordinator.EditFromSnapshotAsync(catalogId, entity.LocalId, entity.Local, entity.Revision, null, token);
        state = await _sync.Store.ReadAsync(catalogId, token) ?? throw new InvalidOperationException(UiText.Get("TodoDeleteRemoteUnavailable"));
        var operation = state.Queue.Single(item => item.EntityId == entity.LocalId && item.Kind == SyncOperationKind.Delete && item.State == SyncOperationState.AwaitingApproval);
        await _sync.Coordinator.ApproveAsync(catalogId, operation.Id, token, operation);
        await _sync.Coordinator.SynchronizeAsync(catalogId, token);
        state = await _sync.Store.ReadAsync(catalogId, token) ?? throw new InvalidOperationException(UiText.Get("TodoDeleteRemotePending"));
        if (state.Queue.Any(item => item.Id == operation.Id) || state.Items.Any(item => item.LocalId == entity.LocalId && item.Baseline is not null))
        {
            _sync.Request(catalogId);
            throw new InvalidOperationException(UiText.Get("TodoDeleteRemotePending"));
        }
    }

    private async Task RefreshLocalAsync()
    {
        if (_reading || _closeToken.IsCancellationRequested) return;
        _reading = true;
        try
        {
            var link = _link;
            if (await FindBindingAsync() is not { } id || _sync is null) return;
            var state = await _sync.Store.ReadAsync(id, _closeToken);
            if (state is null || link != _link) return;
            ApplyLocalState(state);
        }
        catch (OperationCanceledException) when (_closeToken.IsCancellationRequested) { }
        catch (Exception) { Status = UiText.Get("SyncLocalReadFailed"); }
        finally { _reading = false; }
    }

    internal void ApplyLocalState(SyncState state)
    {
        _state = state;
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(SyncStatusText));
        var items = state.Items.Where(e => e.Kind == SyncEntityKind.Item && e.Local is not null).Select(ToItem).ToArray();
        if (state.Cursor is null && state.LastSuccess is null && state.Items.Count == 0 && state.Queue.Count == 0)
        {
            _localItems = null;
            Status = UiText.Get("TodoSyncAwaitingFirstDownload");
        }
        else
        {
            _localItems = items;
            Status = UiText.Format("SyncQueueSummary", items.Length, state.Queue.Count,
                state.Binding.Enabled ? UiText.Get("SyncEnabled") : UiText.Get("SyncPaused"));
        }
        Status = state.Binding.Enabled ? state.ConnectionStatus switch
        {
            SyncConnectionStatus.AuthenticationRequired => UiText.Get("SyncReconnectRequired"),
            SyncConnectionStatus.Forbidden => UiText.Get("TodoSyncAccessDenied"),
            SyncConnectionStatus.Offline => UiText.Get("SyncOffline"),
            SyncConnectionStatus.Throttled => UiText.Get("SyncThrottled"),
            SyncConnectionStatus.Failed => UiText.Get("SyncFailedCachedDataAvailable"),
            SyncConnectionStatus.Synchronizing => UiText.Get("SyncWorking"),
            _ => Status,
        } : UiText.Get("SyncPaused");
        OnPropertyChanged(nameof(Items));
    }

    private async Task AddItemAsync()
    {
        var title = NewItemText.Trim();
        if (title.Length == 0) return;
        if (_sync is null || _state is null || !_state.Binding.Enabled)
        {
            _notifier?.Publish(UiText.Get("TodoSyncEnableFirst"), PanelNotificationSeverity.Warning);
            return;
        }
        var state = _state;
        try
        {
            await _sync.Coordinator.EditAsync(state.Binding.Id, Guid.NewGuid(), new JsonObject { ["title"] = title, ["status"] = "notStarted" }, _closeToken);
            if (NewItemText.Trim() == title) NewItemText = "";
            _sync.Request(state.Binding.Id);
            await RefreshLocalAsync();
        }
        catch (OperationCanceledException) when (_closeToken.IsCancellationRequested) { }
        catch (Exception) { _notifier?.Publish(UiText.Get("SyncLocalReadFailed"), PanelNotificationSeverity.Warning); }
    }

    private async Task ToggleTaskAsync(TodoItemState? item)
    {
        if (item?.Id is not { } itemId || _sync is null || _state is null)
        {
            _notifier?.Publish(UiText.Get("TodoSyncEnableFirst"), PanelNotificationSeverity.Warning);
            OnPropertyChanged(nameof(Items));
            return;
        }
        var state = _state;
        var entity = state.Items.FirstOrDefault(e => e.LocalId == itemId && e.Kind == SyncEntityKind.Item);
        if (entity?.Local is null || entity.Conflicts is { Length: > 0 })
        {
            _notifier?.Publish(UiText.Get("TodoSyncConflict"), PanelNotificationSeverity.Warning);
            OnPropertyChanged(nameof(Items));
            return;
        }
        try
        {
            var desired = (JsonObject)entity.Local.DeepClone();
            desired["status"] = item.IsDone ? "notStarted" : "completed";
            await _sync.Coordinator.EditFromSnapshotAsync(state.Binding.Id, entity.LocalId, entity.Local, entity.Revision, desired, _closeToken);
            _sync.Request(state.Binding.Id);
            await RefreshLocalAsync();
        }
        catch (OperationCanceledException) when (_closeToken.IsCancellationRequested) { }
        catch (Exception) { _notifier?.Publish(UiText.Get("SyncLocalReadFailed"), PanelNotificationSeverity.Warning); await RefreshLocalAsync(); }
        finally { OnPropertyChanged(nameof(Items)); }
    }

    private async Task EditTaskAsync(TodoItemState? item)
    {
        if (item is null || _sync is null || _session is null || _link is null || System.Windows.Application.Current.MainWindow is not { } owner) return;
        try
        {
            var id = await EnsureWritableBindingAsync(owner);
            if (id is null) return;
            var state = await _sync.Store.ReadAsync(id.Value, _closeToken);
            var entity = state?.Items.FirstOrDefault(e => e.Kind == SyncEntityKind.Item &&
                (item.Id is not null && e.LocalId == item.Id || item.RemoteId is not null && e.RemoteId == item.RemoteId));
            if (entity is null && item.RemoteId is not null)
            {
                await _sync.Coordinator.SynchronizeAsync(id.Value, _closeToken);
                state = await _sync.Store.ReadAsync(id.Value, _closeToken);
                entity = state?.Items.FirstOrDefault(e => e.Kind == SyncEntityKind.Item && e.RemoteId == item.RemoteId);
            }
            if (entity?.Local is null || entity.Conflicts is { Length: > 0 })
            {
                _notifier?.Publish(UiText.Get("TodoSyncConflict"), PanelNotificationSeverity.Warning);
                return;
            }
            var desired = SyncEditorWindow.Edit(owner, SyncProviderKind.MicrosoftTodo, entity.Local, metadata: entity.Metadata);
            if (desired is null) return;
            await _sync.Coordinator.EditFromSnapshotAsync(id.Value, entity.LocalId, entity.Local, entity.Revision, desired, _closeToken);
            _sync.Request(id.Value);
            await RefreshLocalAsync();
        }
        catch (OperationCanceledException) when (_closeToken.IsCancellationRequested) { }
        catch (Exception) { _notifier?.Publish(UiText.Get("SyncLocalReadFailed"), PanelNotificationSeverity.Warning); }
    }

    private async Task<Guid?> EnsureWritableBindingAsync(System.Windows.Window owner)
    {
        if (_sync is null || _session is null || _link is null) return null;
        var existing = await FindBindingAsync();
        if (existing is not null) return existing;
        if (UberDashboard.App.Controls.ThemedMessageBox.Show(owner, UiText.Get("SyncReadWriteConsentNotice"),
            UiText.Get("SyncEditorTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return null;
        var credential = await _session.SignInAsync(_link.ClientId, _closeToken, _link.AccountId, allowWrite: true);
        if (credential.Account.HomeAccountId.Identifier != _link.AccountId ||
            !credential.Scopes.Any(s => s.Equals("Tasks.ReadWrite", StringComparison.OrdinalIgnoreCase) || s.EndsWith("/Tasks.ReadWrite", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException();
        return await _sync.EnableAsync(SyncProviderKind.MicrosoftTodo, _link.ClientId.ToString("D"), _link.AccountId,
            "lists/" + Uri.EscapeDataString(_link.ListId) + "/tasks", _snapshot?.Name ?? "Microsoft To Do", _closeToken);
    }

    private async Task SyncNowAsync()
    {
        try
        {
            if (await FindBindingAsync() is { } id && _sync is not null)
            {
                var state = await _sync.Store.ReadAsync(id, _closeToken);
                if (state?.Binding.Enabled != true) { _notifier?.Publish(UiText.Get("SyncPaused"), PanelNotificationSeverity.Warning); return; }
                _sync.Request(id);
                _notifier?.Publish(UiText.Get("TodoSyncRequested"), PanelNotificationSeverity.Information);
            }
            else if (_refresh is not null)
            {
                await _refresh.RefreshAsync();
                if (_refresh.LastError is { } error) _notifier?.Publish(error, PanelNotificationSeverity.Warning);
            }
        }
        catch (OperationCanceledException) when (_closeToken.IsCancellationRequested) { }
        catch (Exception) { _notifier?.Publish(UiText.Get("SyncLocalReadFailed"), PanelNotificationSeverity.Warning); }
    }

    private async Task RemoveCompletedAsync(int keep)
    {
        if (_sync is null || _state is null)
        {
            _notifier?.Publish(UiText.Get("TodoSyncEnableFirst"), PanelNotificationSeverity.Warning);
            return;
        }
        var reviewedState = _state;
        var candidates = reviewedState.Items.Where(e => e.Kind == SyncEntityKind.Item && e.Local is not null)
            .Select((entity, index) => (entity, index, item: ToItem(entity)))
            .Where(x => x.item.IsDone).OrderByDescending(x => x.item.CompletedAt).ThenByDescending(x => x.index).Skip(keep).ToArray();
        if (candidates.Length == 0) return;
        if (candidates.Any(x => x.entity.Conflicts is { Length: > 0 }))
        {
            _notifier?.Publish(UiText.Get("TodoSyncConflict"), PanelNotificationSeverity.Warning);
            return;
        }
        if (UberDashboard.App.Controls.ThemedMessageBox.Show(UiText.Format("TodoSyncConfirmRemoval", candidates.Length), UiText.Get("TodoRemoveTitle"),
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.Cancel) != System.Windows.MessageBoxResult.OK) return;
        var count = 0;
        try
        {
            var latest = await _sync.Store.ReadAsync(reviewedState.Binding.Id, _closeToken);
            var before = reviewedState.Items.Where(e => e.Kind == SyncEntityKind.Item && e.Local is not null).ToArray();
            var after = latest?.Items.Where(e => e.Kind == SyncEntityKind.Item && e.Local is not null).ToArray();
            if (after is null || before.Length != after.Length || before.Any(old => !after.Any(current => current.LocalId == old.LocalId &&
                current.Revision == old.Revision && JsonNode.DeepEquals(current.Local, old.Local))))
                throw new InvalidOperationException("The list changed while deletion was being confirmed.");
            foreach (var candidate in candidates)
            {
                await _sync.Coordinator.EditFromSnapshotAsync(reviewedState.Binding.Id, candidate.entity.LocalId, candidate.entity.Local, candidate.entity.Revision, null, _closeToken);
                count++;
            }
            _sync.Request(reviewedState.Binding.Id);
            _notifier?.Publish(UiText.Format("TodoSyncQueued", count), PanelNotificationSeverity.Information);
        }
        catch (OperationCanceledException) when (_closeToken.IsCancellationRequested) { }
        catch (Exception)
        {
            if (count > 0) _sync.Request(reviewedState.Binding.Id);
            _notifier?.Publish(UiText.Format("TodoSyncPartial", count), PanelNotificationSeverity.Warning);
        }
        await RefreshLocalAsync();
    }

    private async Task OpenSyncEditorAsync()
    {
        if (_sync is null || _session is null || _link is null || System.Windows.Application.Current.MainWindow is not { } owner) return;
        try
        {
            var existing = await EnsureWritableBindingAsync(owner);
            if (existing is null) return;
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
        if (_link != link) { _localItems = null; _state = null; }
        _link = link;
        _snapshot = preferences.Snapshots.FirstOrDefault(item => item.Link == link);
        _snapshotTime = _snapshot?.RefreshedAt;
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(SyncStatusText));
        OnPropertyChanged(nameof(Items));
        var enabled = preferences.ClientId == link.ClientId && preferences.AccountId == link.AccountId &&
            preferences.SelectedLists.Contains(link.ListId);
        if (_state is not null) { ApplyLocalState(_state); return; }
        Status = enabled
            ? UiText.Format("MicrosoftToDoReadOnlyLastRefreshed", (_snapshot?.RefreshedAt ?? preferences.LastSync)?.ToLocalTime())
            : UiText.Get("MicrosoftToDoLatestCopySyncIs");
    }
}
