using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Controls;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Features.Shortcuts;

internal sealed class ShortcutsPanelDescriptor(PanelStateScheduler scheduler) : IPanelDescriptor, IPanelFactory
{
    public const string Id = "shortcuts";
    public string TypeId => Id;
    public string DisplayName => UiText.Get("Shortcuts");
    public string? IconKey => null;
    public MinSize MinSize => new(220, 140);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => this;
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IPanelInstance>(new ShortcutsPanel(context, scheduler));
}

public sealed partial class ShortcutsPanel : ObservableObject, IPanelInstance, IPanelPersistenceObserver
{
    private readonly PanelContext _context;
    private readonly PanelStateScheduler _scheduler;
    private bool _restoring;
    private long _changeVersion;
    public long ChangeVersion => Interlocked.Read(ref _changeVersion);
    public void OnStatePersisted() { }
    [ObservableProperty] private ShortcutsModel _model = new();
    [ObservableProperty] private ShortcutEntry? _selected;
    [ObservableProperty] private ShortcutViewMode _mode;
    [ObservableProperty] private string? _wallpaper;
    [ObservableProperty] private string _status = UiText.Get("ShortcutHint");
    public IReadOnlyList<ShortcutModeOption> Modes { get; } = Enum.GetValues<ShortcutViewMode>()
        .Select(mode => new ShortcutModeOption(mode, UiText.Get("ShortcutView" + mode))).ToArray();
    public PanelInstanceId InstanceId => _context.InstanceId;
    object IPanelInstance.ViewModel => this;

    internal ShortcutsPanel(PanelContext context, PanelStateScheduler scheduler)
    {
        _context = context;
        _scheduler = scheduler;
        Model.Items.CollectionChanged += OnItemsChanged;
    }

    partial void OnModeChanged(ShortcutViewMode value) { Model.Mode = value; Save(); }
    partial void OnWallpaperChanged(string? value) { Model.Wallpaper = value; Save(); }
    private void Save()
    {
        if (_restoring) return;
        Interlocked.Increment(ref _changeVersion);
        _scheduler.RequestSave(InstanceId);
    }
    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Save();
    partial void OnModelChanged(ShortcutsModel? oldValue, ShortcutsModel newValue)
    {
        if (oldValue is not null) oldValue.Items.CollectionChanged -= OnItemsChanged;
        newValue.Items.CollectionChanged += OnItemsChanged;
    }

    [RelayCommand]
    private void Add()
    {
        var draft = ShortcutEditor.Show(null);
        if (draft is null) return;
        Selected = Model.Save(draft);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        if (Selected is not { } original) return;
        var draft = ShortcutEditor.Show(original);
        if (draft is null) return;
        Selected = Model.Save(draft, original);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (Selected is not { } item) return;
        Model.Items.Remove(item);
        Selected = Model.Items.FirstOrDefault();
        _context.Notifier.Publish(UiText.Get("ShortcutRemoved"), PanelNotificationSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))] private void MoveUp() => Move(-1);
    [RelayCommand(CanExecute = nameof(HasSelection))] private void MoveDown() => Move(1);
    private void Move(int offset) { if (Selected is { } item) Model.Move(item, offset); }
    private bool HasSelection() => Selected is not null;
    partial void OnSelectedChanged(ShortcutEntry? value)
    {
        EditCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged(); MoveDownCommand.NotifyCanExecuteChanged(); OpenCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (Selected is not { } item || _context.CloseToken.IsCancellationRequested) return;
        Status = UiText.Get("ShortcutOpening");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _context.CloseToken);
        try
        {
            await Task.Run(() =>
            {
                var target = ShortcutTarget.Normalize(item.Target);
                linked.Token.ThrowIfCancellationRequested();
                if (!target.StartsWith("http:", StringComparison.OrdinalIgnoreCase) &&
                    !target.StartsWith("https:", StringComparison.OrdinalIgnoreCase) && !File.Exists(target) && !Directory.Exists(target))
                    throw new FileNotFoundException();
                linked.Token.ThrowIfCancellationRequested();
                using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }, linked.Token).ConfigureAwait(true);
            Status = UiText.Get("ShortcutOpened");
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { Status = UiText.Get("ShortcutHint"); }
        catch (Exception ex) when (ex is ArgumentException or IOException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            Status = UiText.Get("ShortcutOpenFailed");
            _context.Logger.Log(PanelLogLevel.Error, "Shortcut launch failed.", new Dictionary<string, object?>
            {
                ["operation"] = "shortcut.open", ["errorType"] = ex.GetType().Name,
            });
            _context.Notifier.Publish(Status, PanelNotificationSeverity.Error);
        }
    }

    [RelayCommand]
    private void ChooseWallpaper()
    {
        var dialog = new ThemedFileDialog { Title = UiText.Get("ShortcutWallpaper"), Filter = ShortcutEditor.ImageFilter };
        if (dialog.ShowDialog() == true) Wallpaper = dialog.FileName;
    }
    [RelayCommand] private void ClearWallpaper() => Wallpaper = null;

    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        try
        {
            _restoring = true;
            Model = ShortcutsModel.Restore(state.Span);
            Mode = Model.Mode; Wallpaper = Model.Wallpaper;
            Selected = null;
        }
        catch (JsonException ex)
        {
            _context.Logger.Error("Nie udało się odtworzyć panelu Skróty.", ex);
            Status = UiText.Get("ShortcutRestoreFailed");
            _context.Notifier.Publish(Status, PanelNotificationSeverity.Error);
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }
    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) => ValueTask.FromResult<ReadOnlyMemory<byte>>(Model.Serialize());
    public ValueTask SuspendAsync(CancellationToken cancellationToken) { OpenCommand.Cancel(); return ValueTask.CompletedTask; }
    public ValueTask ResumeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => SuspendAsync(cancellationToken);
    public ValueTask DisposeAsync()
    {
        Model.Items.CollectionChanged -= OnItemsChanged;
        return SuspendAsync(CancellationToken.None);
    }
}

public sealed record ShortcutModeOption(ShortcutViewMode Value, string Label);
