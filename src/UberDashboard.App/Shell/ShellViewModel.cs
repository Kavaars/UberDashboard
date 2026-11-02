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
    private const string DefaultDashboardName = "Mój pulpit";

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
    private readonly IClock _clock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly Dictionary<PanelInstanceId, PanelLeafViewModel> _leaves = [];
    private readonly Dictionary<PanelInstanceId, PanelInstanceRecord> _records = [];
    private readonly List<PanelInstanceId> _visible = [];
    private CancellationTokenSource? _loadCts;
    private LayoutEditor? _editor;
    private PanelInstanceId? _swapSource;
    private DashboardSummary? _selectedDashboard;
    private LayoutNodeViewModel? _root;
    private bool _isEditMode;
    private bool _isBusy;
    private bool _initialized;
    private bool _suppressDashboardReload;
    private string _statusMessage = "Gotowe.";
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
        ILogger<ShellViewModel> logger)
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

        var options = new List<PanelTypeOption>();
        foreach (var registration in _catalog.Registrations)
        {
            options.Add(new PanelTypeOption(registration.Descriptor.TypeId, registration.Descriptor.DisplayName));
        }

        PanelTypes = options;
        Toasts = _toasts.Messages;

        ToggleEditModeCommand = new RelayCommand(ToggleEditMode);
        NewDashboardCommand = new AsyncRelayCommand(NewDashboardAsync);
        ReloadCommand = new AsyncRelayCommand(() => LoadDashboardAsync(CurrentDashboardId, CancellationToken.None));
        ToggleThemeCommand = new AsyncRelayCommand(ToggleThemeAsync);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        SwitchWindowModeCommand = new AsyncRelayCommand<WindowMode>(SwitchWindowModeAsync);
        AddPanelCommand = new AsyncRelayCommand<string>(AddPanelAsync, typeId => !string.IsNullOrWhiteSpace(typeId));
        HidePanelCommand = new AsyncRelayCommand<PanelLeafViewModel>(HidePanelAsync, leaf => leaf is not null);
        RemovePanelCommand = new AsyncRelayCommand<PanelLeafViewModel>(RemovePanelAsync, leaf => leaf is not null);
        SwapPanelCommand = new AsyncRelayCommand<PanelLeafViewModel>(SwapPanelAsync, leaf => leaf is not null);
        RestorePanelCommand = new AsyncRelayCommand<HiddenPanelEntry>(RestorePanelAsync, entry => entry is not null);
        ChangeSplitAxisCommand = new AsyncRelayCommand<PanelSplitViewModel>(ChangeSplitAxisAsync, split => split is not null);
    }

    /// <summary>Pulpity dostępne w selektorze.</summary>
    public ObservableCollection<DashboardSummary> Dashboards { get; } = [];

    /// <summary>Panel ukryte, możliwe do przywrócenia na bieżącym pulpicie.</summary>
    public ObservableCollection<HiddenPanelEntry> HiddenPanels { get; } = [];

    /// <summary>Typy paneli dostępne do dodania.</summary>
    public IReadOnlyList<PanelTypeOption> PanelTypes { get; }

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

    /// <summary>Korzeń drzewa układu bieżącego pulpitu albo <see langword="null"/>.</summary>
    public LayoutNodeViewModel? Root
    {
        get => _root;
        private set
        {
            if (SetProperty(ref _root, value))
            {
                OnPropertyChanged(nameof(IsLayoutEmpty));
            }
        }
    }

    /// <summary><see langword="true"/>, gdy bieżący pulpit nie ma żadnego panelu.</summary>
    public bool IsLayoutEmpty => _root is null;

    /// <summary><see langword="true"/>, gdy istnieje co najmniej jeden ukryty panel.</summary>
    public bool HasHiddenPanels => HiddenPanels.Count > 0;

    /// <summary>Aktualnie aktywny tryb okna głównego.</summary>
    public WindowMode CurrentWindowMode
    {
        get => _currentWindowMode;
        private set => SetProperty(ref _currentWindowMode, value);
    }

    /// <summary><see langword="true"/>, gdy trwa operacja w tle.</summary>
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

            StatusMessage = value ? "Tryb edycji układu włączony." : "Tryb edycji układu wyłączony.";
        }
    }

    /// <summary>Przełącza tryb edycji układu.</summary>
    public IRelayCommand ToggleEditModeCommand { get; }

    /// <summary>Tworzy nowy pulpit i przełącza się na niego.</summary>
    public IAsyncRelayCommand NewDashboardCommand { get; }

    /// <summary>Wczytuje ponownie bieżący pulpit.</summary>
    public IAsyncRelayCommand ReloadCommand { get; }

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

    /// <summary>Zmienia oś podziału (kolumny/wiersze).</summary>
    public IAsyncRelayCommand<PanelSplitViewModel> ChangeSplitAxisCommand { get; }

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
            StatusMessage = "Nie udało się wczytać pulpitów.";
            _toasts.Show("Nie udało się wczytać pulpitów.", ToastSeverity.Error);
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

    private async Task LoadDashboardAsync(Guid dashboardId, CancellationToken cancellationToken)
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
        StatusMessage = "Wczytywanie pulpitu...";

        try
        {
            await UnloadCurrentAsync(token).ConfigureAwait(true);

            var editor = new LayoutEditor(_layouts, _minSizes, _loggerFactory.CreateLogger<LayoutEditor>());
            _editor = editor;

            var load = await editor.LoadAsync(dashboardId, token).ConfigureAwait(true);
            if (!load.IsSuccess)
            {
                _logger.LogWarning(
                    "Układ pulpitu {DashboardId} wczytany z problemem: {Error} — {Message}.",
                    dashboardId,
                    load.Error,
                    load.Message);
                _toasts.Show($"Nie udało się wczytać układu: {load.Error}.", ToastSeverity.Warning);
            }

            var records = await _instances.ListForDashboardAsync(dashboardId, token).ConfigureAwait(true);
            foreach (var record in records)
            {
                _records[record.InstanceId] = record;
            }

            foreach (var record in records)
            {
                if (record.IsHidden)
                {
                    continue;
                }

                _minSizes.Set(record.InstanceId, _catalog.DescriptorOf(record.PanelTypeId)?.MinSize ?? MinSize.Zero);

                var instance = await _host.ActivateAsync(record, token).ConfigureAwait(true);
                if (instance is not null)
                {
                    _visible.Add(record.InstanceId);
                }
            }

            ReconcileLayout();
            editor.SetVisibleInstances(_visible);

            await editor.SaveAsync(force: false, token).ConfigureAwait(true);

            RebuildTree();
            RefreshHiddenPanels();

            StatusMessage = $"Pulpit „{_selectedDashboard?.Name}”: paneli {_visible.Count}, ukrytych {HiddenPanels.Count}.";

            await _dashboards.SetActiveDashboardAsync(dashboardId, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Wczytywanie pulpitu {DashboardId} anulowane.", dashboardId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się wczytać pulpitu {DashboardId}.", dashboardId);
            StatusMessage = "Nie udało się wczytać pulpitu.";
            _toasts.Show("Nie udało się wczytać pulpitu.", ToastSeverity.Error);
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
        foreach (var leaf in _leaves.Values)
        {
            leaf.Dispose();
        }

        _leaves.Clear();
        _records.Clear();
        HiddenPanels.Clear();
        _visible.Clear();
        _swapSource = null;
        Root = null;

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

    private void RebuildTree()
    {
        if (_editor is null)
        {
            Root = null;
            return;
        }

        var active = new List<PanelLeafViewModel>();
        Root = BuildNode(_editor.Tree.Root, active);

        foreach (var instanceId in _leaves.Keys.ToArray())
        {
            var used = false;
            foreach (var leaf in active)
            {
                if (leaf.InstanceId == instanceId)
                {
                    used = true;
                    break;
                }
            }

            if (!used)
            {
                _leaves[instanceId].Dispose();
                _leaves.Remove(instanceId);
            }
        }
    }

    private LayoutNodeViewModel? BuildNode(LayoutNode? node, List<PanelLeafViewModel> active)
    {
        switch (node)
        {
            case null:
                return null;

            case LeafNode leaf:
            {
                var leafViewModel = ResolveLeaf(leaf.InstanceId);
                if (leafViewModel is null)
                {
                    return null;
                }

                active.Add(leafViewModel);
                return leafViewModel;
            }

            case SplitNode split:
            {
                var first = BuildNode(split.First, active);
                var second = BuildNode(split.Second, active);

                if (first is null)
                {
                    return second;
                }

                if (second is null)
                {
                    return first;
                }

                return new PanelSplitViewModel(split.SplitId, split.Axis, split.Ratio, first, second);
            }

            default:
                return null;
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
        if (instance is null)
        {
            return null;
        }

        var leaf = new PanelLeafViewModel(
            instanceId,
            record.PanelTypeId,
            record.DisplayName,
            _catalog.DescriptorOf(record.PanelTypeId)?.IconKey,
            instance.ViewModel)
        {
            IsEditMode = IsEditMode,
        };

        _leaves[instanceId] = leaf;
        return leaf;
    }

    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    private async Task ToggleThemeAsync()
    {
        var kind = _theme.IsDark ? ThemeKind.Light : ThemeKind.Dark;
        await _theme.ApplyAsync(kind, CancellationToken.None).ConfigureAwait(true);
        StatusMessage = $"Motyw: {kind}.";
    }

    private Task OpenSettingsAsync()
    {
        var window = _settingsWindowFactory();
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
            StatusMessage = $"Tryb okna: {result.ActiveMode}.";
            _toasts.Show($"Aktywny tryb: {result.ActiveMode}.", ToastSeverity.Success);
        }
        else
        {
            StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                ? $"Nie udało się aktywować trybu {mode}; powrót do {result.ActiveMode}."
                : result.Message;
            _toasts.Show(StatusMessage, ToastSeverity.Warning);
        }
    }

    /// <summary>Synchronizuje powłokę z aktualnym trybem okna po inicjalizacji usług platformowych.</summary>
    public void SyncWindowMode(WindowMode mode)
    {
        CurrentWindowMode = mode;
        StatusMessage = $"Tryb okna: {mode}.";
    }

    private async Task NewDashboardAsync()
    {
        try
        {
            var name = $"Pulpit {Dashboards.Count + 1}";
            var created = await _dashboards.CreateAsync(name, _clock.UtcNow, CancellationToken.None).ConfigureAwait(true);
            Dashboards.Add(created);
            SelectedDashboard = created;
            _toasts.Show($"Utworzono „{created.Name}“.", ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się utworzyć pulpitu.");
            _toasts.Show("Nie udało się utworzyć pulpitu.", ToastSeverity.Error);
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
            _toasts.Show($"Nieznany typ panelu: {typeId}.", ToastSeverity.Error);
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
                AppServiceCollectionExtensions.BuiltInPluginId,
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

            var instance = await _host.ActivateAsync(record, CancellationToken.None).ConfigureAwait(true);
            if (instance is null)
            {
                await RollbackAsync(instanceId).ConfigureAwait(true);
                _toasts.Show("Nie udało się dodać panelu.", ToastSeverity.Error);
                return;
            }

            var anchor = LayoutWalker.FirstLeaf(_editor.Tree.Root);
            var result = anchor is null
                ? _editor.Restore(instanceId, RestoreHint.None)
                : _editor.Split(anchor.InstanceId, SplitAxis.Horizontal, instanceId, InsertSide.After);

            if (!result.IsSuccess)
            {
                await RollbackAsync(instanceId).ConfigureAwait(true);
                _toasts.Show($"Nie udało się dodać panelu: {result.ErrorMessage}", ToastSeverity.Error);
                return;
            }

            _visible.Add(instanceId);
            _editor.SetVisibleInstances(_visible);

            await SaveLayoutAsync().ConfigureAwait(true);
            RebuildTree();
            _toasts.Show($"Dodano panel „{descriptor.DisplayName}“.", ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd podczas dodawania panelu {PanelTypeId}.", typeId);
            _toasts.Show("Nie udało się dodać panelu.", ToastSeverity.Error);
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
            _toasts.Show($"Nie udało się ukryć panelu: {result.ErrorMessage}", ToastSeverity.Warning);
            return;
        }

        _visible.Remove(leaf.InstanceId);
        _editor.SetVisibleInstances(_visible);

        await _instances
            .SetHiddenAsync(leaf.InstanceId, true, JsonSerializer.Serialize(hint), _clock.UtcNow, CancellationToken.None)
            .ConfigureAwait(true);

        if (_records.TryGetValue(leaf.InstanceId, out var record))
        {
            _records[leaf.InstanceId] = record with
            {
                IsHidden = true,
                RestoreHintJson = JsonSerializer.Serialize(hint),
                UpdatedAtUtc = _clock.UtcNow,
            };
        }

        await _host.SuspendAsync(leaf.InstanceId, CancellationToken.None).ConfigureAwait(true);
        await SaveLayoutAsync().ConfigureAwait(true);

        RebuildTree();
        RefreshHiddenPanels();
        _toasts.Show($"Ukryto panel „{leaf.DisplayName}“.", ToastSeverity.Information);
    }

    private async Task RemovePanelAsync(PanelLeafViewModel? leaf)
    {
        if (_editor is null || leaf is null)
        {
            return;
        }

        var result = _editor.Remove(leaf.InstanceId);
        if (!result.IsSuccess)
        {
            _toasts.Show($"Nie udało się usunąć panelu: {result.ErrorMessage}", ToastSeverity.Warning);
            return;
        }

        _visible.Remove(leaf.InstanceId);
        _editor.SetVisibleInstances(_visible);

        await _host.RemoveAsync(leaf.InstanceId, CancellationToken.None).ConfigureAwait(true);
        await _instances.DeleteAsync(leaf.InstanceId, CancellationToken.None).ConfigureAwait(true);

        _records.Remove(leaf.InstanceId);
        _minSizes.Remove(leaf.InstanceId);

        if (_swapSource == leaf.InstanceId)
        {
            _swapSource = null;
        }

        await SaveLayoutAsync().ConfigureAwait(true);

        RebuildTree();
        RefreshHiddenPanels();
        _toasts.Show($"Usunięto panel „{leaf.DisplayName}“.", ToastSeverity.Information);
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
            StatusMessage = $"Wybrano „{leaf.DisplayName}“ — wskaż drugi panel do zamiany.";
            return;
        }

        if (_swapSource == leaf.InstanceId)
        {
            _swapSource = null;
            StatusMessage = "Anulowano zamianę paneli.";
            return;
        }

        var source = _swapSource.Value;
        var result = _editor.Swap(source, leaf.InstanceId);
        _swapSource = null;

        if (!result.IsSuccess)
        {
            _toasts.Show($"Nie udało się zamienić paneli: {result.ErrorMessage}", ToastSeverity.Warning);
            return;
        }

        await SaveLayoutAsync().ConfigureAwait(true);
        RebuildTree();
        _toasts.Show("Zamieniono panele miejscami.", ToastSeverity.Success);
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
            _toasts.Show("Nie znaleziono ukrytego panelu.", ToastSeverity.Warning);
            return;
        }

        _minSizes.Set(record.InstanceId, _catalog.DescriptorOf(record.PanelTypeId)?.MinSize ?? MinSize.Zero);

        var instance = await _host.ActivateAsync(record, CancellationToken.None).ConfigureAwait(true);
        if (instance is null)
        {
            _toasts.Show("Nie udało się przywrócić panelu.", ToastSeverity.Error);
            return;
        }

        _visible.Add(record.InstanceId);
        _editor.SetVisibleInstances(_visible);

        var hint = DeserializeHint(record.RestoreHintJson);
        var result = _editor.Restore(record.InstanceId, hint);
        if (!result.IsSuccess)
        {
            _visible.Remove(record.InstanceId);
            _editor.SetVisibleInstances(_visible);
            await _host.SuspendAsync(record.InstanceId, CancellationToken.None).ConfigureAwait(true);
            _toasts.Show($"Nie udało się przywrócić panelu: {result.ErrorMessage}", ToastSeverity.Warning);
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

        await SaveLayoutAsync().ConfigureAwait(true);

        RebuildTree();
        RefreshHiddenPanels();

        var message = result.UsedFallback
            ? $"Przywrócono panel „{record.DisplayName}“ w pozycji zastępczej."
            : $"Przywrócono panel „{record.DisplayName}“.";
        _toasts.Show(message, ToastSeverity.Success);
    }

    private async Task ChangeSplitAxisAsync(PanelSplitViewModel? split)
    {
        if (_editor is null || split is null)
        {
            return;
        }

        var result = _editor.ToggleAxis(split.SplitId);
        if (!result.IsSuccess)
        {
            _toasts.Show($"Nie udało się zmienić podziału: {result.ErrorMessage}", ToastSeverity.Warning);
            return;
        }

        await SaveLayoutAsync().ConfigureAwait(true);
        RebuildTree();
        StatusMessage = "Zmieniono orientację podziału.";
    }

    private async Task SaveLayoutAsync()
    {
        if (_editor is null)
        {
            return;
        }

        var result = await _editor.SaveAsync(force: false).ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            _toasts.Show($"Nie zapisano układu: {result.Message}", ToastSeverity.Warning);
        }
    }

    private void RefreshHiddenPanels()
    {
        HiddenPanels.Clear();
        foreach (var record in _records.Values)
        {
            if (record.IsHidden)
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
