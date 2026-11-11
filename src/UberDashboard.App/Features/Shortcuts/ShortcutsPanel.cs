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
using UberDashboard.Core.ModularPanels;

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

public sealed partial class ShortcutsPanel : ObservableObject, IPanelInstance, IPanelPersistenceObserver, IModularPanel
{
    private readonly PanelContext _context;
    private readonly PanelStateScheduler _scheduler;
    private bool _restoring;
    private long _changeVersion;
    public long ChangeVersion => Interlocked.Read(ref _changeVersion);
    public void OnStatePersisted() { }
    [ObservableProperty] private ShortcutsModel _model = new();
    [ObservableProperty] private bool _isModuleEditMode;
    public IReadOnlyList<ModulePosition> ModulePositions => ModuleGrid.Restore(Model.Items.Select(item => item.Id.ToString("D")), Model.ModulePositions, 4);
    public string? GetModuleId(object item) => (item as ShortcutEntry)?.Id.ToString("D");
    public void MoveModuleToCell(string id, int column, int row)
    {
        if (!IsModuleEditMode || ModulePositions.All(position => position.Id != id)) return;
        var positions = ModuleGrid.Move(ModulePositions, id, column, row, 4);
        if (positions.SequenceEqual(ModulePositions)) return;
        Model.ModulePositions = positions;
        OnPropertyChanged(nameof(ModulePositions)); Save();
    }
    public void ResizeModule(string id, int width, int height)
    {
        if (!IsModuleEditMode || ModulePositions.FirstOrDefault(position => position.Id == id) is not { } current) return;
        var updated = current with { Width = Math.Clamp(width, 1, 4), Height = Math.Clamp(height, 1, 16) };
        if (updated == current) return;
        Model.ModulePositions = ModulePositions.Select(position => position.Id == id ? updated : position).ToArray();
        OnPropertyChanged(nameof(ModulePositions)); Save();
    }
    public void MoveModule(object source, object target)
    {
        if (!IsModuleEditMode || source is not ShortcutEntry from || target is not ShortcutEntry to) return;
        var oldIndex = Model.Items.IndexOf(from); var newIndex = Model.Items.IndexOf(to);
        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex) Model.Items.Move(oldIndex, newIndex);
    }
    [ObservableProperty] private ShortcutEntry? _selected;
    [ObservableProperty] private ShortcutViewMode _mode;
    [ObservableProperty] private string? _wallpaper;
    [ObservableProperty] private bool _singleClick;
    [ObservableProperty] private double _moduleSpacing;
    partial void OnModuleSpacingChanged(double value) { Model.ModuleSpacing = value; Save(); }
    [ObservableProperty] private string _status = string.Empty;
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
    partial void OnSingleClickChanged(bool value) { Model.SingleClick = value; Save(); }
    partial void OnWallpaperChanged(string? value) { Model.Wallpaper = value; Save(); }
    private void Save()
    {
        if (_restoring) return;
        Interlocked.Increment(ref _changeVersion);
        _scheduler.RequestSave(InstanceId);
    }
    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { OnPropertyChanged(nameof(ModulePositions)); Save(); }
    partial void OnModelChanged(ShortcutsModel? oldValue, ShortcutsModel newValue)
    {
        if (oldValue is not null) oldValue.Items.CollectionChanged -= OnItemsChanged;
        newValue.Items.CollectionChanged += OnItemsChanged;
        OnPropertyChanged(nameof(ModulePositions));
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
    private void Move(int offset)
    {
        if (Selected is not { } item) return;
        var positions = ModulePositions;
        var ordered = positions.OrderBy(position => position.Row).ThenBy(position => position.Column).ToArray();
        var first = item.Id.ToString("D");
        var index = Array.FindIndex(ordered, position => position.Id == first);
        if (index < 0 || index + offset < 0 || index + offset >= ordered.Length) return;
        var a = ordered[index]; var b = ordered[index + offset];
        Model.ModulePositions = positions.Select(position => position.Id == a.Id ? position with { Column = b.Column, Row = b.Row } :
            position.Id == b.Id ? position with { Column = a.Column, Row = a.Row } : position).ToArray();
        OnPropertyChanged(nameof(ModulePositions)); Save();
    }
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
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { Status = string.Empty; }
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
            Mode = Model.Mode; ModuleSpacing = Model.ModuleSpacing; Wallpaper = Model.Wallpaper; SingleClick = Model.SingleClick ?? Model.Mode == ShortcutViewMode.Buttons;
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
