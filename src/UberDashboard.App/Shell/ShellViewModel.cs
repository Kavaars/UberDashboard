using UberDashboard.Core.Localization;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Layout;
using UberDashboard.Application.Panels;
using UberDashboard.Application.Platform;
using UberDashboard.App.Hosting;
using UberDashboard.App.Panels;
using UberDashboard.App.Settings;
using UberDashboard.App.Theming;
using UberDashboard.App.Toasts;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Shell;

/// <summary>Opcja typu panelu prezentowana w menu „Dodaj panel".</summary>
public sealed record PanelTypeOption(string TypeId, string DisplayName);

/// <summary>Pozycja ukrytego panelu możliwego do przywrócenia na pulpicie.</summary>
public sealed record HiddenPanelEntry(PanelInstanceId InstanceId, string PanelTypeId, string DisplayName);

/// <summary>
/// Model widoku powłoki: spina magazyny, use-case edycji układu i host paneli w
/// jeden spójny ekran. Powłoka nie zna typów paneli — buduje drzewo wyłącznie z
/// kontraktów, a kartę panelu renderuje selektor szablonów warstwy WPF.
/// Wszystkie operacje dyskowe idą przez usługi asynchroniczne, więc wątek UI nie
/// jest blokowany.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private static string DefaultDashboardName => UiText.Get("MyDashboard");

    private readonly IPanelRemovalConfirmation _removalConfirmation;
    private readonly IDashboardStore _dashboards;
    private readonly ILayoutStore _layouts;
    private readonly IPanelInstanceStore _instances;
    private readonly PanelHostManager _host;
    private readonly PanelCatalog _catalog;
    private readonly PanelCatalog.MinSizeSource _minSizes;
    private readonly IToastService _toasts;
    private readonly ThemeService _theme;
    private readonly IWindowModeCoordinator _windowModeCoordinator;
    private readonly Func<SettingsWindow> _settingsWindowFactory;
    private readonly IAppSettingsStore _appearanceStore;
    private readonly Dictionary<PanelInstanceId, GridCell> _cells = [];
    private int _gridColumns;
    private int _gridRows;
    private readonly IClock _clock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly IServiceProvider? _services;
    private readonly Dictionary<PanelInstanceId, PanelLeafViewModel> _leaves = [];
    private readonly Dictionary<PanelInstanceId, PanelGroupSession> _groups = [];
    private readonly HashSet<PanelInstanceId> _groupChildren = [];
    private readonly PanelGroupInitializer? _groupInitializer;
    private readonly IPanelTabGroupStore? _groupStore;
    private readonly Dictionary<PanelInstanceId, PanelAppearanceViewModel> _appearances = [];
    private PanelAppearanceViewModel AppearanceFor(PanelInstanceId id)
    {
        if (_appearances.TryGetValue(id, out var existing)) return existing;
        var appearance = new PanelAppearanceViewModel(id.Value, _appearanceStore, _clock, _toasts);
        _appearances[id] = appearance;
        _ = appearance.LoadAsync();
        return appearance;
    }
    public async Task FlushAppearancesAsync()
    {
        await Task.WhenAll(_appearances.Values.Select(value => value.FlushAsync())).ConfigureAwait(true);
        if (_appearances.Values.Any(value => value.HasSaveError)) throw new System.IO.IOException(UiText.Get("CouldNotSavePanelAppearances"));
    }
    public async Task FlushTabGroupsAsync(CancellationToken cancellationToken)
    {
        foreach (var session in _groups.Values.ToArray()) await session.FlushAsync(cancellationToken).ConfigureAwait(true);
    }
    private readonly Dictionary<PanelInstanceId, PanelInstanceRecord> _records = [];
    private readonly List<PanelInstanceId> _visible = [];
    private CancellationTokenSource? _loadCts;
    private LayoutEditor? _editor;
    private PanelInstanceId? _swapSource;
    private DashboardSummary? _selectedDashboard;
    private bool _isEditMode;
    private bool _isBusy;
    private bool _initialized;
    private bool _suppressDashboardReload;
    private string _statusMessage = UiText.Get("Ready");
    private WindowMode _currentWindowMode = WindowMode.Normal;

    /// <summary>Tworzy model widoku powłoki z usługami hosta.</summary>
    public ShellViewModel(
        IDashboardStore dashboards,
        ILayoutStore layouts,
        IPanelInstanceStore instances,
        PanelHostManager host,
        PanelCatalog catalog,
        PanelCatalog.MinSizeSource minSizes,
        IToastService toasts,
        ThemeService theme,
        IWindowModeCoordinator windowModeCoordinator,
        Func<SettingsWindow> settingsWindowFactory,
        IClock clock,
        ILoggerFactory loggerFactory,
        ILogger<ShellViewModel> logger,
        IPanelRemovalConfirmation removalConfirmation, IAppSettingsStore appearanceStore, PanelGroupInitializer? groupInitializer = null, IPanelTabGroupStore? groupStore = null, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(dashboards);
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(minSizes);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(windowModeCoordinator);
        ArgumentNullException.ThrowIfNull(settingsWindowFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _appearanceStore = appearanceStore; _services = services;
        _groupInitializer = groupInitializer; _groupStore = groupStore;
        _removalConfirmation = removalConfirmation;
        _dashboards = dashboards;
        _layouts = layouts;
        _instances = instances;
        _host = host;
        _catalog = catalog;
        _minSizes = minSizes;
        _toasts = toasts;
        _theme = theme;
        _windowModeCoordinator = windowModeCoordinator;
        _settingsWindowFactory = settingsWindowFactory;
        _clock = clock;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _gridColumns = theme.CurrentSettings.Normalize().GridColumns;
        _gridRows = theme.CurrentSettings.Normalize().GridRows;
        _theme.ThemeChanged += (_, _) => OnGridSettingsChanged();
        Panels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsLayoutEmpty));

        var options = new List<PanelTypeOption>();
        foreach (var registration in _catalog.Registrations)
        {
            if (registration.Descriptor.TypeId == Features.Space.SpacePanelDescriptor.Id) continue;
            options.Add(new PanelTypeOption(registration.Descriptor.TypeId, registration.Descriptor.DisplayName));
        }

        PanelTypes = options;
        Toasts = _toasts.Messages;

        ToggleEditModeCommand = new RelayCommand(ToggleEditMode);
        NewDashboardCommand = new AsyncRelayCommand(NewDashboardAsync);
        RetrySaveLayoutCommand = new AsyncRelayCommand(async () => { if (await SaveLayoutAsync().ConfigureAwait(true)) StatusMessage = UiText.Get("LayoutSaved"); });
        ReloadCommand = new AsyncRelayCommand(() => LoadDashboardAsync(CurrentDashboardId, CancellationToken.None));
        ToggleThemeCommand = new AsyncRelayCommand(ToggleThemeAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        SwitchWindowModeCommand = new AsyncRelayCommand<WindowMode>(SwitchWindowModeAsync);
        AddPanelCommand = new AsyncRelayCommand<string>(AddPanelAsync, typeId => !string.IsNullOrWhiteSpace(typeId));
        HidePanelCommand = new AsyncRelayCommand<PanelLeafViewModel>(HidePanelAsync, leaf => leaf is not null);
        RemovePanelCommand = new AsyncRelayCommand<PanelLeafViewModel>(RemovePanelAsync, leaf => leaf is not null);
        SwapPanelCommand = new AsyncRelayCommand<PanelLeafViewModel>(SwapPanelAsync, leaf => leaf is not null);
        RestorePanelCommand = new AsyncRelayCommand<HiddenPanelEntry>(RestorePanelAsync, entry => entry is not null);
    }

    /// <summary>Pulpity dostępne w selektorze.</summary>
    public ObservableCollection<DashboardSummary> Dashboards { get; } = [];

    /// <summary>Panel ukryte, możliwe do przywrócenia na bieżącym pulpicie.</summary>
    public ObservableCollection<HiddenPanelEntry> HiddenPanels { get; } = [];

    /// <summary>Typy paneli dostępne do dodania.</summary>
    public IReadOnlyList<PanelTypeOption> PanelTypes { get; private set; }
    public void RefreshPanelTypes()
    {
        PanelTypes = _catalog.Registrations.Where(r => r.Descriptor.TypeId != Features.Space.SpacePanelDescriptor.Id).Select(r => new PanelTypeOption(r.Descriptor.TypeId, r.Descriptor.DisplayName)).ToArray();
        OnPropertyChanged(nameof(PanelTypes));
    }

    public async Task RefreshPluginPanelsAsync()
    {
        RefreshPanelTypes();
        if (_initialized && CurrentDashboardId != Guid.Empty)
            await LoadDashboardAsync(CurrentDashboardId, CancellationToken.None);
    }

    /// <summary>Aktywne komunikaty toastów.</summary>
    public ReadOnlyObservableCollection<ToastMessage> Toasts { get; }

    /// <summary>Aktualnie wybrany pulpit; zmiana wyboru wczytuje jego układ.</summary>
    public DashboardSummary? SelectedDashboard
    {
        get => _selectedDashboard;
        set
        {
            if (!SetProperty(ref _selectedDashboard, value) || value is null || _suppressDashboardReload)
            {
                return;
            }

            _ = SwitchDashboardAsync(value.DashboardId);
        }
    }

    /// <summary>Widoczne panele bieżącego pulpitu; każdy zna swój prostokąt na siatce.</summary>
    public ObservableCollection<PanelLeafViewModel> Panels { get; } = [];

    /// <summary><see langword="true"/>, gdy bieżący pulpit nie ma żadnego panelu.</summary>
    public bool IsLayoutEmpty => Panels.Count == 0;

    /// <summary>Liczba kolumn siatki pulpitu z ustawień aplikacji.</summary>
    public int GridColumns { get => _gridColumns; private set => SetProperty(ref _gridColumns, value); }

    /// <summary>Liczba wierszy siatki pulpitu z ustawień aplikacji.</summary>
    public int GridRows { get => _gridRows; private set => SetProperty(ref _gridRows, value); }

    /// <summary><see langword="true"/>, gdy istnieje co najmniej jeden ukryty panel.</summary>
    public bool HasHiddenPanels => HiddenPanels.Count > 0;

    /// <summary>Aktualnie aktywny tryb okna głównego.</summary>
    public WindowMode CurrentWindowMode
    {
        get => _currentWindowMode;
        private set
        {
            if (SetProperty(ref _currentWindowMode, value))
            {
                OnPropertyChanged(nameof(IsDockedBar));
                OnPropertyChanged(nameof(WindowModeLabel));
            }
        }
    }

    /// <summary><see langword="true"/>, gdy trwa operacja w tle.</summary>
    public string WindowModeLabel => Controls.SettingsValueLabelConverter.Label(CurrentWindowMode);

    public bool IsDockedBar => CurrentWindowMode == WindowMode.DockedBar;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Krótki opis bieżącego stanu powłoki.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Tryb edycji układu: odsłania akcje modyfikujące drzewo.</summary>
    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (!SetProperty(ref _isEditMode, value))
            {
                return;
            }

            foreach (var leaf in _leaves.Values)
            {
                leaf.IsEditMode = value;
            }

            StatusMessage = value ? UiText.Get("LayoutEditingEnabled") : UiText.Get("LayoutEditingDisabled");
        }
    }

    /// <summary>Przełącza tryb edycji układu.</summary>
    public IRelayCommand ToggleEditModeCommand { get; }

    /// <summary>Tworzy nowy pulpit i przełącza się na niego.</summary>
    public IAsyncRelayCommand NewDashboardCommand { get; }

    /// <summary>Wczytuje ponownie bieżący pulpit.</summary>
    public IAsyncRelayCommand ReloadCommand { get; }
    public IAsyncRelayCommand RetrySaveLayoutCommand { get; }

    /// <summary>Przełącza motyw jasny/ciemny.</summary>
    public IAsyncRelayCommand ToggleThemeCommand { get; }

    /// <summary>Otwiera okno ustawień aplikacji.</summary>
    public IAsyncRelayCommand OpenSettingsCommand { get; }

    /// <summary>Przełącza tryb pracy głównego okna.</summary>
    public IAsyncRelayCommand<WindowMode> SwitchWindowModeCommand { get; }

    /// <summary>Dodaje nowy panel wskazanego typu do bieżącego pulpitu.</summary>
    public IAsyncRelayCommand<string> AddPanelCommand { get; }

    /// <summary>Ukrywa panel, zachowując jego dane i pozycję do przywrócenia.</summary>
    public IAsyncRelayCommand<PanelLeafViewModel> HidePanelCommand { get; }

    /// <summary>Trwale usuwa panel wraz z jego stanem.</summary>
    public IAsyncRelayCommand<PanelLeafViewModel> RemovePanelCommand { get; }

    /// <summary>Zamienia miejscami dwa panele wskazane kolejno przez użytkownika.</summary>
    public IAsyncRelayCommand<PanelLeafViewModel> SwapPanelCommand { get; }

    /// <summary>Przywraca ukryty panel na bieżącym pulpicie.</summary>
    public IAsyncRelayCommand<HiddenPanelEntry> RestorePanelCommand { get; }

    /// <summary>
    /// Wczytuje listę pulpitów, wybiera aktywny i buduje pierwszy układ. Operacja
    /// jest idempotentna — kolejne wywołania są ignorowane.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        try
        {
            var dashboards = await _dashboards.ListAsync(cancellationToken).ConfigureAwait(true);
            if (dashboards.Count == 0)
            {
                await _dashboards.CreateAsync(DefaultDashboardName, _clock.UtcNow, cancellationToken).ConfigureAwait(true);
                dashboards = await _dashboards.ListAsync(cancellationToken).ConfigureAwait(true);
            }

            _suppressDashboardReload = true;
            try
            {
                Dashboards.Clear();
                foreach (var dashboard in dashboards)
                {
                    Dashboards.Add(dashboard);
                }

                var activeId = await _dashboards.GetActiveDashboardIdAsync(cancellationToken).ConfigureAwait(true);
                SelectedDashboard = ResolveActive(dashboards, activeId);
            }
            finally
            {
                _suppressDashboardReload = false;
            }

            await LoadDashboardAsync(SelectedDashboard!.DashboardId, cancellationToken).ConfigureAwait(true);
            CurrentWindowMode = _windowModeCoordinator.CurrentMode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się zainicjować powłoki.");
            StatusMessage = UiText.Get("CouldNotLoadDashboards");
            _toasts.Show(UiText.Get("CouldNotLoadDashboards"), ToastSeverity.Error);
        }
    }

    private Guid CurrentDashboardId => _selectedDashboard?.DashboardId ?? Guid.Empty;

    private static DashboardSummary ResolveActive(IReadOnlyList<DashboardSummary> dashboards, Guid? activeId)
    {
        if (activeId is { } id)
        {
            foreach (var dashboard in dashboards)
            {
                if (dashboard.DashboardId == id)
                {
                    return dashboard;
                }
            }
        }

        return dashboards[0];
    }

    private async Task SwitchDashboardAsync(Guid dashboardId)
    {
        try
        {
            await LoadDashboardAsync(dashboardId, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się przełączyć na pulpit {DashboardId}.", dashboardId);
        }
    }

    private async Task LoadDashboardAsync(Guid dashboardId, CancellationToken cancellationToken, bool propagateFailure = false)
    {
        if (dashboardId == Guid.Empty)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _loadCts.Token;

        IsBusy = true;
        StatusMessage = UiText.Get("LoadingDashboard");

        try
        {
            await UnloadCurrentAsync(token).ConfigureAwait(true);

            var editor = new LayoutEditor(_layouts, _minSizes, _loggerFactory.CreateLogger<LayoutEditor>());
            _editor = editor;

            var load = await editor.LoadAsync(dashboardId, token).ConfigureAwait(true);
            if (!load.IsSuccess)
            {
                if (propagateFailure) throw new InvalidOperationException(UiText.Format("CouldNotLoadLayout", load.Message));
                _logger.LogWarning(
                    "Układ pulpitu {DashboardId} wczytany z problemem: {Error} — {Message}.",
                    dashboardId,
                    load.Error,
                    load.Message);
                _toasts.Show(UiText.Format("CouldNotLoadLayout2", load.Error), ToastSeverity.Warning);
            }

            var records = await _instances.ListForDashboardAsync(dashboardId, token).ConfigureAwait(true);
            foreach (var record in records) _records[record.InstanceId] = record;
            if (_groupStore is not null)
                _groupChildren.UnionWith(await _groupStore.GetChildInstanceIdsAsync(dashboardId, token).ConfigureAwait(true));

            foreach (var record in records)
            {
                // Puste miejsca są teraz wolnymi komórkami siatki, więc dawne panele odstępu pomijamy.
                if (_groupChildren.Contains(record.InstanceId) || record.IsHidden || record.PanelTypeId == Features.Space.SpacePanelDescriptor.Id)
                {
                    continue;
                }

                _minSizes.Set(record.InstanceId, _catalog.DescriptorOf(record.PanelTypeId)?.MinSize ?? new MinSize(280, 200));

                await ActivatePanelAsync(record, token).ConfigureAwait(true);
                _visible.Add(record.InstanceId);
            }

            var savedGrid = await ReadGridAsync(dashboardId, token).ConfigureAwait(true);
            if (savedGrid is null)
                foreach (var (id, cell) in PanelGrid.FromTree(editor.Tree.Root, GridColumns, GridRows)) _cells[id] = cell;
            else
                foreach (var (id, cell) in savedGrid.Cells) _cells[new PanelInstanceId(id)] = PanelGrid.Rescale(cell, savedGrid.Columns, savedGrid.Rows, GridColumns, GridRows);
            ReconcileLayout();
            editor.SetVisibleInstances(_visible);
            var gridChanged = SettleCells() || savedGrid is null || savedGrid.Columns != GridColumns || savedGrid.Rows != GridRows;

            var savedLayout = await editor.SaveAsync(force: false, token).ConfigureAwait(true);
            if (propagateFailure && !savedLayout.IsSuccess) throw new InvalidOperationException(UiText.Format("CouldNotSaveLayout", savedLayout.Message));

            RebuildPanels();
            RefreshHiddenPanels();
            if (gridChanged) await SaveGridAsync().ConfigureAwait(true);

            StatusMessage = UiText.Format("DashboardPanelsHidden", _selectedDashboard?.Name, _visible.Count, HiddenPanels.Count);

            await _dashboards.SetActiveDashboardAsync(dashboardId, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Wczytywanie pulpitu {DashboardId} anulowane.", dashboardId);
            if (propagateFailure) throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się wczytać pulpitu {DashboardId}.", dashboardId);
            StatusMessage = UiText.Get("CouldNotLoadTheDashboard");
            _toasts.Show(UiText.Get("CouldNotLoadTheDashboard"), ToastSeverity.Error);
            if (propagateFailure) throw;
        }
        finally
        {
            if (_loadCts is not null && _loadCts.Token == token)
            {
                IsBusy = false;
            }
        }
    }

    private async Task UnloadCurrentAsync(CancellationToken cancellationToken)
    {
        // Poczekaj na operacje zakładek, zanim zwolnisz ich widoki i hosty.
        var suspended = new List<PanelGroupSession>();
        try
        {
            foreach (var session in _groups.Values.ToArray())
            {
                if (await session.SuspendAsync(cancellationToken).ConfigureAwait(true)) suspended.Add(session);
            }
        }
        catch
        {
            foreach (var session in suspended)
            {
                try { await session.ActivateAsync(CancellationToken.None).ConfigureAwait(true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Nie udało się wznowić panelu po przerwaniu przełączania pulpitu."); }
            }
            throw;
        }
        foreach (var leaf in _leaves.Values)
        {
            leaf.Dispose();
        }

        _leaves.Clear();
        _groups.Clear();
        _groupChildren.Clear();
        _records.Clear();
        HiddenPanels.Clear();
        _visible.Clear();
        _swapSource = null;
        _cells.Clear();
        Panels.Clear();

        var active = new List<PanelInstanceId>(_host.ActiveInstances);
        foreach (var instanceId in active)
        {
            await _host.SuspendAsync(instanceId, cancellationToken).ConfigureAwait(true);
        }

        _editor?.Reset();
        _editor = null;

        OnPropertyChanged(nameof(HasHiddenPanels));
    }

    private void ReconcileLayout()
    {
        if (_editor is null)
        {
            return;
        }

        if (_editor.Tree.IsEmpty)
        {
            foreach (var instanceId in _visible)
            {
                _editor.Restore(instanceId, RestoreHint.None);
            }

            return;
        }

        foreach (var instanceId in LayoutWalker.Instances(_editor.Tree.Root))
        {
            if (!_visible.Contains(instanceId))
            {
                _editor.Remove(instanceId);
            }
        }

        foreach (var instanceId in _visible)
        {
            if (!LayoutWalker.Contains(_editor.Tree.Root, instanceId))
            {
                _editor.Restore(instanceId, RestoreHint.None);
            }
        }
    }

    private (int Width, int Height) DefaultSpan => (Math.Max(1, (GridColumns + 2) / 4), Math.Max(1, (GridRows + 1) / 2));

    private Dictionary<PanelInstanceId, GridCell> VisibleCells() => _cells.Where(pair => _visible.Contains(pair.Key)).ToDictionary();

    /// <summary>Prostokąt widocznego panelu albo <see langword="null"/>.</summary>
    internal GridCell? CellOf(PanelInstanceId id) => _visible.Contains(id) && _cells.TryGetValue(id, out var cell) ? cell : null;

    /// <summary>Przyciąga panel do wskazanych komórek, jeśli są wolne; zapis wykonuje <see cref="SaveGridAsync"/>.</summary>
    internal bool PlacePanel(PanelInstanceId id, GridCell cell)
    {
        if (!IsEditMode || IsBusy || !_visible.Contains(id) || !PanelGrid.Fits(VisibleCells(), id, cell, GridColumns, GridRows)) return false;
        _cells[id] = cell;
        if (_leaves.TryGetValue(id, out var leaf)) leaf.Cell = cell;
        return true;
    }

    /// <summary>Usuwa nakładanie i wyjścia poza siatkę; panele bez miejsca dostają pierwsze wolne komórki.</summary>
    private bool SettleCells()
    {
        var accepted = new Dictionary<PanelInstanceId, GridCell>();
        var changed = false;
        foreach (var id in _visible)
        {
            var known = _cells.TryGetValue(id, out var current);
            var cell = known && PanelGrid.Fits(accepted, id, current, GridColumns, GridRows)
                ? current
                // Pełna siatka: nakładanie jest lepsze niż zgubienie panelu; użytkownik może go przesunąć.
                : PanelGrid.FindFree(accepted, DefaultSpan.Width, DefaultSpan.Height, GridColumns, GridRows) ?? new GridCell(0, 0, 1, 1);
            changed |= !known || cell != current;
            _cells[id] = accepted[id] = cell;
        }
        return changed;
    }

    private void OnGridSettingsChanged()
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) { dispatcher.BeginInvoke(OnGridSettingsChanged); return; }
        var (columns, rows) = (_theme.CurrentSettings.GridColumns, _theme.CurrentSettings.GridRows);
        if (columns == GridColumns && rows == GridRows) return;
        // shortcut: kolejne przeskalowania sumują zaokrąglenia; gdyby panele dryfowały, pamiętaj układ bazowy sprzed podglądu.
        foreach (var id in _cells.Keys.ToArray()) _cells[id] = PanelGrid.Rescale(_cells[id], GridColumns, GridRows, columns, rows);
        GridColumns = columns;
        GridRows = rows;
        SettleCells();
        RebuildPanels();
        _ = SaveGridAsync();
    }

    private sealed record GridDocument(int Columns, int Rows, Dictionary<Guid, GridCell> Cells);

    private static string GridKey(Guid dashboardId) => $"dashboard.grid.{dashboardId:N}";

    private async Task<GridDocument?> ReadGridAsync(Guid dashboardId, CancellationToken token)
    {
        try
        {
            var raw = await _appearanceStore.GetAsync(GridKey(dashboardId), token).ConfigureAwait(true);
            return raw is null ? null : JsonSerializer.Deserialize<GridDocument>(raw);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Nie udało się odczytać siatki pulpitu {DashboardId}; układ zostanie odtworzony.", dashboardId);
            return null;
        }
    }

    /// <summary>Utrwala pozycje paneli bieżącego pulpitu.</summary>
    internal async Task SaveGridAsync()
    {
        if (CurrentDashboardId == Guid.Empty) return;
        try
        {
            var document = new GridDocument(GridColumns, GridRows, _cells.ToDictionary(pair => pair.Key.Value, pair => pair.Value));
            await _appearanceStore.SetAsync(GridKey(CurrentDashboardId), JsonSerializer.Serialize(document), _clock.UtcNow).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zapis siatki pulpitu nie powiódł się.");
            _toasts.Show(UiText.Format("CouldNotSaveTheLayout", ex.Message), ToastSeverity.Warning);
        }
    }

    private void RebuildPanels()
    {
        var active = new List<PanelLeafViewModel>();
        foreach (var id in LayoutWalker.Instances(_editor?.Tree.Root))
        {
            if (ResolveLeaf(id) is not { } leaf) continue;
            leaf.Cell = _cells.TryGetValue(id, out var cell) ? cell : new GridCell(0, 0, 1, 1);
            active.Add(leaf);
        }

        foreach (var stale in Panels.Except(active).ToArray()) Panels.Remove(stale);
        foreach (var leaf in active) if (!Panels.Contains(leaf)) Panels.Add(leaf);
        foreach (var instanceId in _leaves.Keys.ToArray())
        {
            if (active.Any(leaf => leaf.InstanceId == instanceId)) continue;
            _leaves[instanceId].Dispose();
            _leaves.Remove(instanceId);
        }
    }

    private PanelLeafViewModel? ResolveLeaf(PanelInstanceId instanceId)
    {
        if (_leaves.TryGetValue(instanceId, out var cached))
        {
            return cached;
        }

        if (!_records.TryGetValue(instanceId, out var record))
        {
            return null;
        }

        var instance = _host.Find(instanceId);
        _groups.TryGetValue(instanceId, out var session);
        var leaf = new PanelLeafViewModel(
            instanceId,
            record.PanelTypeId,
            record.DisplayName,
            _catalog.DescriptorOf(record.PanelTypeId)?.IconKey,
            session?.ActiveViewModel ?? instance?.ViewModel ?? _host.Unavailable(record))
        {
            Appearance = AppearanceFor(instanceId),
            IsEditMode = IsEditMode,
            Minimum = _catalog.Contains(record.PanelTypeId) ? _minSizes.MinimumOf(instanceId) : new MinSize(280, 200),
        };

        leaf.RemoveCommand = new AsyncRelayCommand(() => RemovePanelAsync(leaf));
        if (session is not null) leaf.SetTabSession(session);
        _leaves[instanceId] = leaf;
        return leaf;
    }

    private async Task ActivatePanelAsync(PanelInstanceRecord root, CancellationToken token)
    {
        if (root.PanelTypeId == Features.Space.SpacePanelDescriptor.Id || _groupInitializer is null || _groupStore is null)
        {
            await _host.ActivateAsync(root, token).ConfigureAwait(true);
            return;
        }
        try
        {
            if (_catalog.DescriptorOf(root.PanelTypeId) is { } descriptor && root.DataSchemaVersion > descriptor.StateSchemaVersion &&
                await _groupStore.GetAsync(root.InstanceId, token).ConfigureAwait(true) is null)
            {
                await _host.ActivateAsync(root, token).ConfigureAwait(true);
                return;
            }
            var group = await _groupInitializer.EnsureAsync(root, token).ConfigureAwait(true);
            foreach (var member in await _groupInitializer.ReadMembersAsync(group, token).ConfigureAwait(true))
            {
                _records[member.InstanceId] = member;
                if (member.InstanceId != root.InstanceId) _groupChildren.Add(member.InstanceId);
            }
            var session = new PanelGroupSession(root, group, _groupStore, _instances, _host, _clock, _toasts);
            if (_services?.GetService(typeof(Features.Todo.MicrosoftTodoSettings)) is Features.Todo.MicrosoftTodoSettings todoSettings)
                session.ConfigureTodoNotifications(todoSettings, _appearanceStore);
            await session.ActivateAsync(token).ConfigureAwait(true);
            _groups[root.InstanceId] = session;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się odtworzyć zakładek panelu {PanelId}. Dane pozostawiono bez zmian.", root.InstanceId);
            _toasts.Show(UiText.Get("CouldNotRestorePanelTabsSavedData"), ToastSeverity.Error);
            if (ex is JsonException) _host.MarkDataError(root.InstanceId);
        }
    }

    private async Task SuspendPanelAsync(PanelInstanceId id, CancellationToken token)
    {
        if (_groups.TryGetValue(id, out var session)) await session.SuspendAsync(token).ConfigureAwait(true);
        else await _host.SuspendAsync(id, token).ConfigureAwait(true);
    }

    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    private async Task ToggleThemeAsync()
    {
        var kind = _theme.IsDark ? ThemeKind.Light : ThemeKind.Dark;
        await _theme.ApplyAsync(kind, CancellationToken.None).ConfigureAwait(true);
        StatusMessage = UiText.Format("Theme2", Controls.SettingsValueLabelConverter.Label(kind));
    }

    private Task OpenSettingsAsync()
    {
        if (System.Windows.Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault() is { } existing)
        {
            existing.Activate();
            return Task.CompletedTask;
        }
        var window = _settingsWindowFactory();
        window.TodoTargets = () => _groups.Values.Where(group => group.ImportsTasks && !group.IsClosed)
            .OrderBy(group => group.ImportDisplayName).ToArray();
        if (System.Windows.Application.Current?.MainWindow is { } mainWindow && !ReferenceEquals(mainWindow, window))
        {
            window.Owner = mainWindow;
        }

        window.Show();
        window.Activate();
        return Task.CompletedTask;
    }

    private async Task SwitchWindowModeAsync(WindowMode mode)
    {
        var result = await _windowModeCoordinator.SwitchAsync(mode, CancellationToken.None).ConfigureAwait(true);
        CurrentWindowMode = result.ActiveMode;
        if (result.IsSuccess)
        {
            StatusMessage = UiText.Format("WindowMode2", WindowModeLabel);
            _toasts.Show(UiText.Format("ActiveMode2", WindowModeLabel), ToastSeverity.Success);
        }
        else
        {
            StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                ? UiText.Format("CouldNotActivateReturnedTo", Controls.SettingsValueLabelConverter.Label(mode), WindowModeLabel)
                : result.Message;
            _toasts.Show(StatusMessage, ToastSeverity.Warning);
        }
    }

    /// <summary>Synchronizuje powłokę z aktualnym trybem okna po inicjalizacji usług platformowych.</summary>
    public void SyncWindowMode(WindowMode mode)
    {
        CurrentWindowMode = mode;
        StatusMessage = UiText.Format("WindowMode2", WindowModeLabel);
    }

    public async Task DeleteDashboardAsync(DashboardSummary dashboard)
    {
        if (IsBusy || Dashboards.Count <= 1 || !Dashboards.Contains(dashboard)) return;
        var active = dashboard.DashboardId == CurrentDashboardId;
        if (active && Controls.ThemedMessageBox.Show(UiText.Format("DeleteTheActiveDashboardItsPanelsAnd", dashboard.Name), UiText.Get("DeleteDashboard2"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        try
        {
            if (active)
            {
                var next = Dashboards.First(item => item.DashboardId != dashboard.DashboardId);
                await FlushAppearancesAsync().ConfigureAwait(true);
                await FlushTabGroupsAsync(CancellationToken.None).ConfigureAwait(true);
                if (!await SaveLayoutAsync().ConfigureAwait(true)) return;
                try { await LoadDashboardAsync(next.DashboardId, CancellationToken.None, true).ConfigureAwait(true); }
                catch
                {
                    try { await LoadDashboardAsync(dashboard.DashboardId, CancellationToken.None, true).ConfigureAwait(true); }
                    catch (Exception restoreError) { _logger.LogError(restoreError, "Nie udało się przywrócić poprzedniego pulpitu."); }
                    throw;
                }
                _suppressDashboardReload = true;
                try { SelectedDashboard = next; } finally { _suppressDashboardReload = false; }
            }
            await _dashboards.DeleteAsync(dashboard.DashboardId, CancellationToken.None).ConfigureAwait(true);
            Dashboards.Remove(dashboard);
            _toasts.Show(UiText.Format("DeletedDashboard", dashboard.Name), ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się usunąć pulpitu {DashboardId}.", dashboard.DashboardId);
            _toasts.Show(UiText.Get("CouldNotDeleteTheDashboardCheckThat"), ToastSeverity.Error);
        }
    }

    private async Task NewDashboardAsync()
    {
        try
        {
            var name = UiText.Format("Dashboard", Dashboards.Count + 1);
            var created = await _dashboards.CreateAsync(name, _clock.UtcNow, CancellationToken.None).ConfigureAwait(true);
            Dashboards.Add(created);
            SelectedDashboard = created;
            _toasts.Show(UiText.Format("Created", created.Name), ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się utworzyć pulpitu.");
            _toasts.Show(UiText.Get("CouldNotCreateADashboard"), ToastSeverity.Error);
        }
    }

    private async Task AddPanelAsync(string? typeId)
    {
        if (_editor is null || _selectedDashboard is null || string.IsNullOrWhiteSpace(typeId))
        {
            return;
        }

        var descriptor = _catalog.DescriptorOf(typeId);
        if (descriptor is null)
        {
            _toasts.Show(UiText.Format("UnknownPanelType", typeId), ToastSeverity.Error);
            return;
        }

        if (PanelGrid.FindFree(VisibleCells(), DefaultSpan.Width, DefaultSpan.Height, GridColumns, GridRows) is not { } cell)
        {
            _toasts.Show(UiText.Get("NoFreeSpaceOnTheGrid"), ToastSeverity.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            var now = _clock.UtcNow;
            var instanceId = PanelInstanceId.New();
            var record = new PanelInstanceRecord(
                instanceId,
                _selectedDashboard.DashboardId,
                _catalog.Find(descriptor.TypeId)!.PluginId,
                descriptor.TypeId,
                descriptor.DisplayName,
                false,
                null,
                null,
                null,
                descriptor.StateSchemaVersion,
                now,
                now);

            await _instances.UpsertAsync(record, CancellationToken.None).ConfigureAwait(true);
            _records[instanceId] = record;
            _minSizes.Set(instanceId, descriptor.MinSize);

            await ActivatePanelAsync(record, CancellationToken.None).ConfigureAwait(true);
            var instance = _groups.TryGetValue(instanceId, out var createdGroup) ? _host.Find(createdGroup.Group.ActiveId) : _host.Find(instanceId);
            if (instance is null)
            {
                await RollbackAsync(instanceId).ConfigureAwait(true);
                _toasts.Show(UiText.Get("CouldNotAddThePanel"), ToastSeverity.Error);
                return;
            }

            var result = _editor.Restore(instanceId, RestoreHint.None);
            if (!result.IsSuccess)
            {
                await RollbackAsync(instanceId).ConfigureAwait(true);
                _toasts.Show(UiText.Format("CouldNotAddThePanel2", result.ErrorMessage), ToastSeverity.Error);
                return;
            }

            _cells[instanceId] = cell;
            _visible.Add(instanceId);
            _editor.SetVisibleInstances(_visible);

            await SaveGridAsync().ConfigureAwait(true);
            if (!await SaveLayoutAsync().ConfigureAwait(true)) { RebuildPanels(); RefreshHiddenPanels(); return; }
            RebuildPanels();
            RefreshHiddenPanels();
            _toasts.Show(UiText.Format("AddedPanel", descriptor.DisplayName), ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas dodawania panelu {PanelTypeId}.", typeId);
            _toasts.Show(UiText.Get("CouldNotAddThePanel"), ToastSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RollbackAsync(PanelInstanceId instanceId)
    {
        await _host.RemoveAsync(instanceId, CancellationToken.None).ConfigureAwait(true);
        await _instances.DeleteAsync(instanceId, CancellationToken.None).ConfigureAwait(true);
        _records.Remove(instanceId);
        _groups.Remove(instanceId);
        _groupChildren.Remove(instanceId);
        _minSizes.Remove(instanceId);
        _visible.Remove(instanceId);
    }

    private async Task HidePanelAsync(PanelLeafViewModel? leaf)
    {
        if (_editor is null || leaf is null)
        {
            return;
        }

        var hint = _editor.CaptureRestoreHint(leaf.InstanceId);
        var result = _editor.Remove(leaf.InstanceId);
        if (!result.IsSuccess)
        {
            _toasts.Show(UiText.Format("CouldNotHideThePanel", result.ErrorMessage), ToastSeverity.Warning);
            return;
        }

        _visible.Remove(leaf.InstanceId);
        _editor.SetVisibleInstances(_visible);

        try
        {
            // Nie zmieniaj flagi ukrycia, dopóki bieżąca treść nie zostanie utrwalona.
            await SuspendPanelAsync(leaf.InstanceId, CancellationToken.None).ConfigureAwait(true);
            await _instances.SetHiddenAsync(leaf.InstanceId, true, JsonSerializer.Serialize(hint), _clock.UtcNow, CancellationToken.None).ConfigureAwait(true);
            if (_records.TryGetValue(leaf.InstanceId, out var record))
                _records[leaf.InstanceId] = record with { IsHidden = true, RestoreHintJson = JsonSerializer.Serialize(hint), UpdatedAtUtc = _clock.UtcNow };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się ukryć panelu {PanelId}.", leaf.InstanceId);
            if (_groups.TryGetValue(leaf.InstanceId, out var session) && !_host.IsActive(session.Group.ActiveId))
                await session.ActivateAsync(CancellationToken.None).ConfigureAwait(true);
            _visible.Add(leaf.InstanceId);
            _editor.SetVisibleInstances(_visible);
            var restore = _editor.Restore(leaf.InstanceId, hint);
            if (!restore.IsSuccess) _logger.LogError("Nie udało się przywrócić położenia panelu {PanelId}: {Error}.", leaf.InstanceId, restore.ErrorMessage);
            RebuildPanels(); RefreshHiddenPanels();
            _toasts.Show(UiText.Get("ThePanelCouldNotBeHiddenIts"), ToastSeverity.Error);
            return;
        }
        if (!await SaveLayoutAsync().ConfigureAwait(true)) { RebuildPanels(); RefreshHiddenPanels(); return; }

        RebuildPanels();
        RefreshHiddenPanels();
        _toasts.Show(UiText.Format("HidPanel", leaf.DisplayName), ToastSeverity.Information);
    }

    private async Task RemovePanelAsync(PanelLeafViewModel? leaf)
    {
        if (_editor is null || leaf is null)
        {
            return;
        }

        if (!_removalConfirmation.Confirm(leaf.DisplayName))
        {
            return;
        }

        var hint = _editor.CaptureRestoreHint(leaf.InstanceId);
        var result = _editor.Remove(leaf.InstanceId);
        if (!result.IsSuccess)
        {
            _toasts.Show(UiText.Format("CouldNotDeleteThePanel", result.ErrorMessage), ToastSeverity.Warning);
            return;
        }

        _visible.Remove(leaf.InstanceId);
        _editor.SetVisibleInstances(_visible);

        try
        {
            if (_groups.TryGetValue(leaf.InstanceId, out var removedGroup) && _groupStore is not null)
            {
                await removedGroup.DeleteGroupAsync(CancellationToken.None).ConfigureAwait(true);
                foreach (var tab in removedGroup.Group.Tabs) { _groupChildren.Remove(tab.InstanceId); _records.Remove(tab.InstanceId); }
                _groups.Remove(leaf.InstanceId);
            }
            else
            {
                await _host.RemoveAsync(leaf.InstanceId, CancellationToken.None).ConfigureAwait(true);
                await _instances.DeleteAsync(leaf.InstanceId, CancellationToken.None).ConfigureAwait(true);
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się usunąć panelu {PanelId}.", leaf.InstanceId);
            _visible.Add(leaf.InstanceId);
            _editor.SetVisibleInstances(_visible);
            var restore = _editor.Restore(leaf.InstanceId, hint);
            if (!restore.IsSuccess) _logger.LogError("Nie udało się odtworzyć położenia panelu {PanelId}: {Error}.", leaf.InstanceId, restore.ErrorMessage);
            RebuildPanels(); RefreshHiddenPanels();
            _toasts.Show(UiText.Get("ThePanelCouldNotBeDeletedIts"), ToastSeverity.Error);
            return;
        }

        _records.Remove(leaf.InstanceId);
        _minSizes.Remove(leaf.InstanceId);
        _cells.Remove(leaf.InstanceId);
        await SaveGridAsync().ConfigureAwait(true);

        if (_swapSource == leaf.InstanceId)
        {
            _swapSource = null;
        }

        if (!await SaveLayoutAsync().ConfigureAwait(true)) { RebuildPanels(); RefreshHiddenPanels(); return; }

        RebuildPanels();
        RefreshHiddenPanels();
        _toasts.Show(UiText.Format("DeletedPanel", leaf.DisplayName), ToastSeverity.Information);
    }

    private async Task SwapPanelAsync(PanelLeafViewModel? leaf)
    {
        if (_editor is null || leaf is null)
        {
            return;
        }

        if (_swapSource is null)
        {
            _swapSource = leaf.InstanceId;
            StatusMessage = UiText.Format("SelectedChooseTheOtherPanelToSwap", leaf.DisplayName);
            return;
        }

        if (_swapSource == leaf.InstanceId)
        {
            _swapSource = null;
            StatusMessage = UiText.Get("PanelSwapCanceled");
            return;
        }

        var source = _swapSource.Value;
        _swapSource = null;
        if (CellOf(source) is not { } first || CellOf(leaf.InstanceId) is not { } second)
        {
            _toasts.Show(UiText.Format("CouldNotSwapPanels", leaf.DisplayName), ToastSeverity.Warning);
            return;
        }

        // Zamiana całych prostokątów nie może nałożyć się na inne panele: zajmują ten sam obszar co przedtem.
        _cells[source] = second;
        _cells[leaf.InstanceId] = first;
        RebuildPanels();
        await SaveGridAsync().ConfigureAwait(true);
        _toasts.Show(UiText.Get("SwappedPanelPositions"), ToastSeverity.Success);
    }

    private async Task RestorePanelAsync(HiddenPanelEntry? entry)
    {
        if (_editor is null || entry is null)
        {
            return;
        }

        var record = await _instances.GetAsync(entry.InstanceId, CancellationToken.None).ConfigureAwait(true);
        if (record is null)
        {
            _toasts.Show(UiText.Get("TheHiddenPanelCouldNotBeFound"), ToastSeverity.Warning);
            return;
        }

        var remembered = _cells.TryGetValue(record.InstanceId, out var known) && PanelGrid.Fits(VisibleCells(), record.InstanceId, known, GridColumns, GridRows);
        if ((remembered ? known : PanelGrid.FindFree(VisibleCells(), DefaultSpan.Width, DefaultSpan.Height, GridColumns, GridRows)) is not { } cell)
        {
            _toasts.Show(UiText.Get("NoFreeSpaceOnTheGrid"), ToastSeverity.Warning);
            return;
        }

        _minSizes.Set(record.InstanceId, _catalog.DescriptorOf(record.PanelTypeId)?.MinSize ?? new MinSize(280, 200));

        await ActivatePanelAsync(record, CancellationToken.None).ConfigureAwait(true);
        _visible.Add(record.InstanceId);
        _editor.SetVisibleInstances(_visible);

        var hint = DeserializeHint(record.RestoreHintJson);
        var result = _editor.Restore(record.InstanceId, hint);
        if (!result.IsSuccess)
        {
            _visible.Remove(record.InstanceId);
            _editor.SetVisibleInstances(_visible);
            await SuspendPanelAsync(record.InstanceId, CancellationToken.None).ConfigureAwait(true);
            _toasts.Show(UiText.Format("CouldNotRestoreThePanel", result.ErrorMessage), ToastSeverity.Warning);
            return;
        }

        await _instances
            .SetHiddenAsync(record.InstanceId, false, null, _clock.UtcNow, CancellationToken.None)
            .ConfigureAwait(true);

        _records[record.InstanceId] = record with
        {
            IsHidden = false,
            RestoreHintJson = null,
            UpdatedAtUtc = _clock.UtcNow,
        };

        _cells[record.InstanceId] = cell;
        await SaveGridAsync().ConfigureAwait(true);
        if (!await SaveLayoutAsync().ConfigureAwait(true)) { RebuildPanels(); RefreshHiddenPanels(); return; }

        RebuildPanels();
        RefreshHiddenPanels();

        var message = !remembered
            ? UiText.Format("RestoredPanelInADifferentPosition", record.DisplayName)
            : UiText.Format("RestoredPanel", record.DisplayName);
        _toasts.Show(message, ToastSeverity.Success);
    }

    private async Task<bool> SaveLayoutAsync()
    {
        if (_editor is null) return false;
        try
        {
            var result = await _editor.SaveAsync(force: false).ConfigureAwait(true);
            if (result.IsSuccess) return true;
            StatusMessage = UiText.Get("TheLayoutWasNotSavedSelectSave");
            _toasts.Show(UiText.Format("CouldNotSaveTheLayout", result.Message), ToastSeverity.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zapis układu nie powiódł się.");
            StatusMessage = UiText.Get("TheLayoutWasNotSavedSelectSave");
            _toasts.Show(StatusMessage, ToastSeverity.Error);
        }
        return false;
    }
    private void RefreshHiddenPanels()
    {
        HiddenPanels.Clear();
        foreach (var record in _records.Values)
        {
            if (record.IsHidden && !_groupChildren.Contains(record.InstanceId) && record.PanelTypeId != Features.Space.SpacePanelDescriptor.Id)
            {
                HiddenPanels.Add(new HiddenPanelEntry(record.InstanceId, record.PanelTypeId, record.DisplayName));
            }
        }

        OnPropertyChanged(nameof(HasHiddenPanels));
    }

    private static RestoreHint DeserializeHint(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return RestoreHint.None;
        }

        try
        {
            return JsonSerializer.Deserialize<RestoreHint>(json) ?? RestoreHint.None;
        }
        catch (JsonException)
        {
            return RestoreHint.None;
        }
    }
}
