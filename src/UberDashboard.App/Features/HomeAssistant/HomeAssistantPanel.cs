using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Panels;
using UberDashboard.Application.HomeAssistant;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Localization;
using UberDashboard.Core.ModularPanels;

namespace UberDashboard.App.Features.HomeAssistant;

internal sealed class HomeAssistantPanelDescriptor(PanelStateScheduler scheduler, IHomeAssistantService service) : IPanelDescriptor, IPanelFactory
{
    public const string Id = "homeassistant";
    public string TypeId => Id;
    public string DisplayName => UiText.Get("HomeAssistant");
    public string? IconKey => null;
    public MinSize MinSize => new(220, 140);
    public int StateSchemaVersion => HomeAssistantPanelDocument.SchemaVersion;
    public IPanelFactory Factory => this;
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IPanelInstance>(new HomeAssistantPanel(context, scheduler, service));
}

/// <summary>Moduł na panelu: odczyt stanu encji albo akcja (przycisk).</summary>
public sealed partial class HomeAssistantModuleItem : ObservableObject
{
    public required string Id { get; init; }
    public required string EntityId { get; init; }
    public required string Label { get; init; }
    public required HomeAssistantModuleKind Kind { get; init; }
    public bool IsAction => Kind == HomeAssistantModuleKind.Action;
    [ObservableProperty] private string _value = "";
}

public sealed partial class HomeAssistantPanel : ObservableObject, IPanelInstance, IPanelPersistenceObserver, IModularPanel
{
    private readonly PanelContext _context;
    private readonly PanelStateScheduler _scheduler;
    private readonly IHomeAssistantService _service;
    private bool _restoring;
    private long _changeVersion;
    private ModulePosition[] _positions = [];
    private string? _protectedToken;
    private CancellationTokenSource? _loop;

    public long ChangeVersion => Interlocked.Read(ref _changeVersion);
    public void OnStatePersisted() { }

    public ObservableCollection<HomeAssistantModuleItem> Modules { get; } = [];
    [ObservableProperty] private bool _isModuleEditMode;
    [ObservableProperty] private string? _baseUrl;
    [ObservableProperty] private double _moduleSpacing;
    [ObservableProperty] private string _status = string.Empty;
    /// <summary>Interwał odświeżania odczytów w sekundach (5–3600).</summary>
    public int RefreshSeconds { get; private set; } = 30;

    public IReadOnlyList<ModulePosition> ModulePositions => ModuleGrid.Restore(Modules.Select(module => module.Id), _positions, 4);
    public PanelInstanceId InstanceId => _context.InstanceId;
    object IPanelInstance.ViewModel => this;

    internal HomeAssistantPanel(PanelContext context, PanelStateScheduler scheduler, IHomeAssistantService service)
    {
        _context = context;
        _scheduler = scheduler;
        _service = service;
        Modules.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(ModulePositions)); Save(); };
    }

    public string? GetModuleId(object item) => (item as HomeAssistantModuleItem)?.Id;

    public void MoveModuleToCell(string id, int column, int row)
    {
        if (!IsModuleEditMode || ModulePositions.All(position => position.Id != id)) return;
        var positions = ModuleGrid.Move(ModulePositions, id, column, row, 4);
        if (positions.SequenceEqual(ModulePositions)) return;
        _positions = positions.ToArray();
        OnPropertyChanged(nameof(ModulePositions)); Save();
    }

    partial void OnModuleSpacingChanged(double value) => Save();
    partial void OnBaseUrlChanged(string? value) => Save();

    private void Save()
    {
        if (_restoring) return;
        Interlocked.Increment(ref _changeVersion);
        _scheduler.RequestSave(InstanceId);
    }

    [RelayCommand]
    private void AddModule()
    {
        var module = HomeAssistantEditor.ShowModule();
        if (module is null) return;
        Modules.Add(ToItem(module));
        RestartLoop();
    }

    [RelayCommand]
    private void OpenConnection()
    {
        var result = HomeAssistantEditor.ShowConnection(BaseUrl);
        if (result is null) return;
        BaseUrl = result.Value.Url;
        if (result.Value.Token is { } token) _protectedToken = HomeAssistantTokenProtector.Protect(token);
        Save();
        RestartLoop();
    }

    [RelayCommand]
    private void RemoveModule(HomeAssistantModuleItem? item)
    {
        if (item is null || !Modules.Remove(item)) return;
        _context.Notifier.Publish(UiText.Get("HomeAssistantModuleRemoved"), PanelNotificationSeverity.Success);
    }

    [RelayCommand]
    private async Task PressAsync(HomeAssistantModuleItem? item)
    {
        if (item is null || !item.IsAction || !TryConnection(out var connection)) return;
        try
        {
            await _service.CallServiceAsync(connection, item.EntityId, _context.CloseToken).ConfigureAwait(true);
            Status = UiText.Get("HomeAssistantActionSent");
        }
        catch (Exception ex) when (IsServiceFailure(ex) && !_context.CloseToken.IsCancellationRequested)
        {
            Status = UiText.Get("HomeAssistantActionFailed");
            LogFailure("homeassistant.press", ex);
            _context.Notifier.Publish(Status, PanelNotificationSeverity.Error);
        }
    }

    public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask RestoreStateAsync(ReadOnlyMemory<byte> state, CancellationToken cancellationToken)
    {
        try
        {
            var document = JsonSerializer.Deserialize<HomeAssistantPanelDocument>(state.Span) ?? new();
            _restoring = true;
            _positions = document.ModulePositions;
            ModuleSpacing = document.ModuleSpacing;
            BaseUrl = document.BaseUrl;
            RefreshSeconds = Math.Clamp(document.RefreshSeconds, 5, 3600);
            _protectedToken = document.ProtectedToken;
            Modules.Clear();
            foreach (var module in document.Modules) Modules.Add(ToItem(module));
            IsModuleEditMode = false;
        }
        catch (JsonException ex)
        {
            LogFailure("homeassistant.restore", ex);
            Status = UiText.Get("HomeAssistantRestoreFailed");
            _context.Notifier.Publish(Status, PanelNotificationSeverity.Error);
        }
        finally { _restoring = false; }
        return ValueTask.CompletedTask;
    }

    public ValueTask<ReadOnlyMemory<byte>> CaptureStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<ReadOnlyMemory<byte>>(JsonSerializer.SerializeToUtf8Bytes(new HomeAssistantPanelDocument
        {
            ModulePositions = ModulePositions.ToArray(),
            ModuleSpacing = ModuleSpacing,
            BaseUrl = BaseUrl,
            ProtectedToken = _protectedToken,
            RefreshSeconds = RefreshSeconds,
            Modules = Modules.Select(module => new HomeAssistantModuleState(module.Id, module.EntityId, module.Label, module.Kind)).ToArray(),
        }));

    public ValueTask SuspendAsync(CancellationToken cancellationToken) { StopLoop(); return ValueTask.CompletedTask; }
    public ValueTask ResumeAsync(CancellationToken cancellationToken) { RestartLoop(); return ValueTask.CompletedTask; }
    public ValueTask PrepareForRemovalAsync(CancellationToken cancellationToken) => SuspendAsync(cancellationToken);
    public ValueTask DisposeAsync() { StopLoop(); return ValueTask.CompletedTask; }

    private static HomeAssistantModuleItem ToItem(HomeAssistantModuleState state) =>
        new() { Id = state.Id, EntityId = state.EntityId, Label = state.Label, Kind = state.Kind };

    private static bool IsServiceFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or ArgumentException or InvalidOperationException;

    // Logs only the operation and exception type: messages can echo the address, and the token must never reach a log.
    private void LogFailure(string operation, Exception ex) => _context.Logger.Log(PanelLogLevel.Error, "Operacja Home Assistant nie powiodła się.",
        new Dictionary<string, object?> { ["operation"] = operation, ["errorType"] = ex.GetType().Name });

    private bool TryConnection(out HomeAssistantConnection connection)
    {
        connection = null!;
        var token = HomeAssistantTokenProtector.Unprotect(_protectedToken);
        if (BaseUrl is null || token is null)
        {
            Status = UiText.Get("HomeAssistantNotConnected");
            return false;
        }
        try
        {
            connection = new HomeAssistantConnection(HomeAssistantValidation.NormalizeBaseUrl(BaseUrl), token);
            return true;
        }
        catch (ArgumentException)
        {
            Status = UiText.Get("HomeAssistantNotConnected");
            return false;
        }
    }

    private void RestartLoop()
    {
        StopLoop();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_context.CloseToken);
        _loop = cancellation;
        _ = RunLoopAsync(cancellation.Token);
    }

    // shortcut: values refresh by polling every RefreshSeconds, no WebSocket, upgrade if instant updates are needed.
    private void StopLoop()
    {
        _loop?.Cancel();
        _loop = null;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(RefreshSeconds));
            do
            {
                await RefreshValuesAsync(cancellationToken).ConfigureAwait(true);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshValuesAsync(CancellationToken cancellationToken)
    {
        if (!TryConnection(out var connection)) return;
        var failed = false;
        foreach (var item in Modules.Where(module => !module.IsAction).ToArray())
        {
            try
            {
                var state = await _service.GetStateAsync(connection, item.EntityId, cancellationToken).ConfigureAwait(true);
                item.Value = string.IsNullOrEmpty(state.Unit) ? state.State : $"{state.State} {state.Unit}";
            }
            catch (Exception ex) when (IsServiceFailure(ex) && !cancellationToken.IsCancellationRequested)
            {
                item.Value = "—";
                failed = true;
            }
        }
        Status = failed ? UiText.Get("HomeAssistantOffline") : string.Empty;
    }
}
