using UberDashboard.Core.Localization;
using UberDashboard.App.Panels;
using UberDashboard.Core.ModularPanels;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Resources;

internal sealed class ResourcePanelDescriptor(HardwareSampler sampler) : IPanelDescriptor, IPanelFactory
{
    public const string Id = "system-resources";
    public string TypeId => Id;
    public string DisplayName => UiText.Get("ResourceMonitor");
    public string? IconKey => null;
    public MinSize MinSize => new(270, 150);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory => this;

    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IPanelInstance>(new ResourcePanel(context, sampler));
}

public sealed partial class ResourcePanel : ObservableObject, IPanelInstance, IPanelStateNotifications, IModularPanel
{
    private readonly PanelContext _context;
    private readonly HardwareSampler _sampler;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _reportedError;
    private readonly List<string> _moduleOrder = [];
    private IReadOnlyList<ModulePosition> _modulePositions = [];
    // Wszystkie moduły mają ten sam rozmiar, więc zapisany rozmiar z dawnych wersji jest ignorowany.
    public IReadOnlyList<ModulePosition> ModulePositions => ModuleGrid.Restore(_moduleOrder, _modulePositions, 4).Select(position => position with { Width = 0, Height = 1 }).ToArray();
    public string? GetModuleId(object item) => (item as ResourceReading)?.Key;
    public void MoveModuleToCell(string id, int column, int row)
    {
        if (!IsModuleEditMode || ModulePositions.All(position => position.Id != id)) return;
        var positions = ModuleGrid.Move(ModulePositions, id, column, row, 4);
        if (positions.SequenceEqual(ModulePositions)) return;
        _modulePositions = positions;
        OnPropertyChanged(nameof(ModulePositions));
        ChangeVersion++; StateChanged?.Invoke(this, EventArgs.Empty);
    }
    [ObservableProperty] private bool _isModuleEditMode;
    /// <summary>Zmiana rozmiaru modułu jest wyłączona: wszystkie moduły mają jednakowy rozmiar.</summary>
    public void ResizeModule(string id, int width, int height) { }
    public void MoveModule(object source, object target)
    {
        if (!IsModuleEditMode || source is not ResourceReading from || target is not ResourceReading to || from.Key == to.Key) return;
        var oldIndex = _moduleOrder.IndexOf(from.Key); var newIndex = _moduleOrder.IndexOf(to.Key);
        if (oldIndex < 0 || newIndex < 0) return;
        _moduleOrder.RemoveAt(oldIndex); _moduleOrder.Insert(newIndex, from.Key);
        UpdateReadings();
        ChangeVersion++; StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private readonly HashSet<string> _hidden = new(StringComparer.Ordinal);
    private IReadOnlyList<ResourceReading> _allReadings = [];
    public ObservableCollection<ResourceOption> Options { get; } = [];
    public event EventHandler? StateChanged;
    public long ChangeVersion { get; private set; }
    public void OnStatePersisted() { }

    [ObservableProperty] private IReadOnlyList<ResourceReading> _readings = [];
    [ObservableProperty] private bool _showTiles;
    [ObservableProperty] private double _moduleSpacing;
    partial void OnModuleSpacingChanged(double value)
    {
        if (_restoring) return;
        ChangeVersion++; StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private bool _restoring;
    partial void OnShowTilesChanged(bool value)
    {
        if (_restoring) return;
        ChangeVersion++;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    [ObservableProperty] private string _status = UiText.Get("ReadingSensors");

    public PanelInstanceId InstanceId => _context.InstanceId;
    object IPanelInstance.ViewModel => this;

    internal ResourcePanel(PanelContext context, HardwareSampler sampler)
    {
        _context = context;
        _sampler = sampler;
        _timer.Tick += OnTick;
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ResumeAsync(cancellationToken);
    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ResourcePanelState>(state.Span);
            _hidden.Clear();
            _moduleOrder.Clear();
            _modulePositions = document?.ModulePositions ?? [];
            _moduleOrder.AddRange((document?.ModuleOrder ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal));
            _restoring = true;
            ShowTiles = document?.ShowTiles ?? false;
            ModuleSpacing = Math.Clamp(document?.ModuleSpacing ?? 0, -8, 24);
            _restoring = false;
            foreach (var key in document?.Hidden ?? []) _hidden.Add(key);
            UpdateReadings();
        }
        catch (JsonException ex) { _context.Logger.Error("Nie udało się odtworzyć ustawień monitora zasobów.", ex); }
        return ValueTask.CompletedTask;
    }
    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ReadOnlyMemory<byte>>(JsonSerializer.SerializeToUtf8Bytes(new ResourcePanelState { Hidden = [.. _hidden], ShowTiles = ShowTiles, ModuleSpacing = ModuleSpacing, ModuleOrder = [.. _moduleOrder], ModulePositions = [.. ModulePositions] }));

    private void UpdateReadings()
    {
        foreach (var reading in _allReadings)
            if (!Options.Any(option => option.Key == reading.Key))
                Options.Add(new ResourceOption(reading.Key, $"{reading.Name} — {reading.Device}", !_hidden.Contains(reading.Key), OptionChanged));
        foreach (var option in Options) option.SetVisible(!_hidden.Contains(option.Key));
        foreach (var reading in _allReadings)
            if (!_moduleOrder.Contains(reading.Key, StringComparer.Ordinal)) _moduleOrder.Add(reading.Key);
        OnPropertyChanged(nameof(ModulePositions));
        var ranks = _moduleOrder.Select((key, index) => (key, index)).ToDictionary(x => x.key, x => x.index, StringComparer.Ordinal);
        Readings = _allReadings.Where(reading => !_hidden.Contains(reading.Key)).OrderBy(reading => ranks[reading.Key]).ToArray();
    }

    private void OptionChanged(ResourceOption option)
    {
        if (option.IsVisible) _hidden.Remove(option.Key); else _hidden.Add(option.Key);
        UpdateReadings();
        ChangeVersion++;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask SuspendAsync(CancellationToken cancellationToken)
    {
        _timer.Stop();
        SampleCommand.Cancel();
        return ValueTask.CompletedTask;
    }

    public ValueTask ResumeAsync(CancellationToken cancellationToken)
    {
        if (_context.CloseToken.IsCancellationRequested) return ValueTask.CompletedTask;
        OnTick(null, EventArgs.Empty);
        _timer.Start();
        return ValueTask.CompletedTask;
    }

    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => SuspendAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _timer.Tick -= OnTick;
        return SuspendAsync(CancellationToken.None);
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (SampleCommand.CanExecute(null)) SampleCommand.Execute(null);
    }

    [RelayCommand]
    private async Task SampleAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _context.CloseToken);
        try
        {
            var result = await Task.Run(() => _sampler.Sample(linked.Token), linked.Token).ConfigureAwait(true);
            linked.Token.ThrowIfCancellationRequested();
            _allReadings = result;
            UpdateReadings();
            Status = string.Empty;
            _reportedError = false;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _allReadings = [];
            Readings = [];
            Status = UiText.Get("CouldNotReadSensorsTheMonitorWill");
            if (!_reportedError)
            {
                _context.Logger.Error("Nie udało się odczytać czujników sprzętowych.", ex);
                _reportedError = true;
            }
        }
    }
}

internal sealed class ResourcePanelState
{
    public string[] Hidden { get; set; } = [];
    public bool ShowTiles { get; set; }
    public double ModuleSpacing { get; set; }
    public string[] ModuleOrder { get; set; } = [];
    public ModulePosition[] ModulePositions { get; set; } = [];
}

public sealed partial class ResourceOption : ObservableObject
{
    private readonly Action<ResourceOption> _changed;
    private bool _updating;
    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isVisible;
    internal ResourceOption(string key, string label, bool isVisible, Action<ResourceOption> changed)
    { Key = key; Label = label; _isVisible = isVisible; _changed = changed; }
    internal void SetVisible(bool value) { _updating = true; IsVisible = value; _updating = false; }
    partial void OnIsVisibleChanged(bool value) { if (!_updating) _changed(this); }
}
