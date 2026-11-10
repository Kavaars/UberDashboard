using UberDashboard.Core.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Panels;
using UberDashboard.Application.Weather;
using UberDashboard.Contracts.Host;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.ModularPanels;

namespace UberDashboard.App.Features.Weather;

/// <summary>Gotowy do renderowania wpis prognozy godzinowej.</summary>
public sealed record WeatherHourlyForecastDisplayItem(
    string TimeText,
    string Glyph,
    string ColorGlyph,
    string TemperatureText,
    string ConditionText,
    double TemperatureCelsius);

/// <summary>
/// Model widoku panelu pogody z wyszukiwaniem lokalizacji, odświeżaniem i fallbackiem do cache.
/// </summary>
public sealed partial class WeatherPanelViewModel : ObservableObject, IDisposable, IPanelOptionsPresenter, IModularPanel
{
    private static readonly string[] DefaultModuleIds = ["location", "icon", "temperature", "range", "sunrise", "wind", "updated", "forecast"];
    private IReadOnlyList<ModulePosition> _modulePositions = [];
    [ObservableProperty] private bool _isModuleEditMode;
    [ObservableProperty] private double _moduleSpacing;
    partial void OnModuleSpacingChanged(double value) => Changed?.Invoke(this, EventArgs.Empty);
    public IReadOnlyList<WeatherModule> Modules { get; }
    public IReadOnlyList<ModulePosition> ModulePositions => ModuleGrid.Restore(DefaultModuleIds, _modulePositions, 4);
    public string? GetModuleId(object item) => (item as WeatherModule)?.Id;
    public void MoveModuleToCell(string id, int column, int row)
    {
        if (!IsModuleEditMode || ModulePositions.All(position => position.Id != id)) return;
        var moved = ModuleGrid.Move(ModulePositions, id, column, row, 4);
        if (moved.SequenceEqual(ModulePositions)) return;
        _modulePositions = moved; OnPropertyChanged(nameof(ModulePositions)); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void MoveModule(object source, object target)
    {
        if (source is not WeatherModule from || target is not WeatherModule to) return;
        if (ModulePositions.FirstOrDefault(position => position.Id == to.Id) is { } position) MoveModuleToCell(from.Id, position.Column, position.Row);
    }
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromMinutes(30);

    private readonly IWeatherService _weatherService;
    private readonly IWeatherLocationSearchService _locationSearchService;
    private readonly IWeatherCacheStore _cacheStore;
    private readonly IPanelNotifier _notifier;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _autoRefreshCts;
    private bool _disposed;
    private CancellationTokenSource _networkCts = new();
    private DateTimeOffset? _networkConsentGrantedAtUtc;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool isNetworkEnabled;

    partial void OnIsNetworkEnabledChanged(bool value)
    {
        _networkCts.Cancel();
        _networkCts.Dispose();
        _networkCts = new CancellationTokenSource();
        _networkConsentGrantedAtUtc = value ? DateTimeOffset.UtcNow : null;
        CancelSearch();
        ClearSearchResults();
        IsSearching = false;
        ScheduleAutoRefresh();
        StatusText = value ? UiText.Get("ConnectionsEnabledChooseALocationOrRefresh") : UiText.Get("WeatherIsOfflineAllowConnectionsInThe");
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private bool _suppressSearch;

    /// <summary>Tworzy model widoku panelu pogody.</summary>
    public WeatherPanelViewModel(
        IWeatherService weatherService,
        IWeatherLocationSearchService locationSearchService,
        IWeatherCacheStore cacheStore,
        IPanelNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(weatherService);
        ArgumentNullException.ThrowIfNull(locationSearchService);
        ArgumentNullException.ThrowIfNull(cacheStore);
        ArgumentNullException.ThrowIfNull(notifier);

        _weatherService = weatherService;
        _locationSearchService = locationSearchService;
        _cacheStore = cacheStore;
        _notifier = notifier;

        Modules = DefaultModuleIds.Select(id => new WeatherModule(id, this)).ToArray();
        SearchResults = [];
        HourlyForecast = [];
        RefreshCommand = new AsyncRelayCommand(
            execute: () => RefreshAsync(CancellationToken.None),
            canExecute: CanRefresh);
        SelectLocationCommand = new AsyncRelayCommand<WeatherLocationOption>(SelectLocationAsync, location => location is not null);
        ClearLocationCommand = new RelayCommand(ClearLocation, () => SelectedLocation is not null);
        ToggleOptionsCommand = new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);

        OpenSourceCommand = new RelayCommand<string>(OpenSource);
        StatusText = UiText.Get("ChooseALocationToFetchWeather");
        Title = UiText.Get("Weather");
    }

    /// <summary>Podnoszone, gdy zmieni się konfiguracja wymagająca zapisu.</summary>
    public event EventHandler? Changed;

    /// <summary>Wyniki bieżącego wyszukiwania lokalizacji.</summary>
    public ObservableCollection<WeatherLocationOption> SearchResults { get; }

    /// <summary>Prognoza godzinowa przygotowana do wyświetlenia na karcie.</summary>
    public ObservableCollection<WeatherHourlyForecastDisplayItem> HourlyForecast { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearLocationCommand))]
    private WeatherLocationOption? selectedLocation;

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private string conditionText = UiText.Get("NoData");

    [ObservableProperty]
    private string conditionGlyph = "•";

    [ObservableProperty]
    private string colorConditionGlyph = "•";

    [ObservableProperty]
    private string windText = "—";

    [ObservableProperty]
    private string updatedText = "—";

    [ObservableProperty]
    private string dailyRangeText = "—";

    [ObservableProperty]
    private string sunriseText = "—";

    [ObservableProperty]
    private string sunsetText = "—";

    [ObservableProperty]
    private double? temperatureCelsius;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private bool isStale;

    [ObservableProperty]
    private bool isOptionsOpen;

    [ObservableProperty]
    private int autoRefreshMinutes = 30;

    [ObservableProperty]
    private bool colorfulTheme;

    [ObservableProperty] private bool showLocation = true;
    [ObservableProperty] private bool showIcon = true;
    [ObservableProperty] private bool showTemperature = true;
    [ObservableProperty] private bool showCondition = true;
    [ObservableProperty] private bool showDailyRange = true;
    [ObservableProperty] private bool showSunrise = true;
    [ObservableProperty] private bool showSunset = true;
    [ObservableProperty] private bool showForecast = true;

    [ObservableProperty]
    private bool showWind = true;

    [ObservableProperty]
    private bool showUpdatedTime = true;

    /// <summary><see langword="true"/>, gdy panel ma wybraną lokalizację.</summary>
    public bool HasLocation => SelectedLocation is not null;

    /// <summary><see langword="true"/>, gdy panel ma dane do wyświetlenia.</summary>
    public bool HasSnapshot => TemperatureCelsius.HasValue;

    /// <summary><see langword="true"/>, gdy panel ma rozszerzone dane dobowe.</summary>
    public bool HasDailyDetails =>
        !string.Equals(DailyRangeText, "—", StringComparison.Ordinal)
        || !string.Equals(SunriseText, "—", StringComparison.Ordinal)
        || !string.Equals(SunsetText, "—", StringComparison.Ordinal);

    /// <summary><see langword="true"/>, gdy dostępna jest prognoza godzinowa.</summary>
    public bool HasHourlyForecast => HourlyForecast.Count > 0;

    /// <summary>Panel pogody udostępnia pływające opcje.</summary>
    public bool HasOptions => true;

    /// <summary>Ręczne odświeżenie bieżącej lokalizacji.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>Wybiera lokalizację z listy wyników i pobiera dane.</summary>
    public IAsyncRelayCommand<WeatherLocationOption> SelectLocationCommand { get; }

    /// <summary>Czyści wybraną lokalizację i bieżące dane.</summary>
    public IRelayCommand ClearLocationCommand { get; }

    /// <summary>Przełącza pływające opcje panelu.</summary>
    public IRelayCommand ToggleOptionsCommand { get; }

    partial void OnSelectedLocationChanged(WeatherLocationOption? value)
    {
        Title = value is null ? UiText.Get("Weather") : UiText.Format("Weather2", value.Name);
        OnPropertyChanged(nameof(HasLocation));
        ScheduleAutoRefresh();
    }

    partial void OnTemperatureCelsiusChanged(double? value) => OnPropertyChanged(nameof(HasSnapshot));

    partial void OnSearchTextChanged(string value)
    {
        if (_suppressSearch)
        {
            return;
        }

        _ = SearchAsync(value);
    }

    partial void OnAutoRefreshMinutesChanged(int value)
    {
        ScheduleAutoRefresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnColorfulThemeChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowLocationChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowIconChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowTemperatureChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowConditionChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowDailyRangeChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowSunriseChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowSunsetChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);
    partial void OnShowForecastChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnShowWindChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnShowUpdatedTimeChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Wczytuje zapisany stan panelu.</summary>
    public void Load(WeatherPanelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var savedPositions = (document.ModulePositions ?? []).OfType<ModulePosition>().ToArray();
        _modulePositions = savedPositions.Any(position => position.Id == "sunrise")
            ? savedPositions
            : savedPositions.Select(position => position.Id == "sunset" ? position with { Id = "sunrise" } : position).ToArray();
        ModuleSpacing = Math.Clamp(document.ModuleSpacing, -8, 24);
        OnPropertyChanged(nameof(ModulePositions));

        _suppressSearch = true;
        try
        {
            SearchText = document.SelectedLocation?.DisplayName ?? string.Empty;
        }
        finally
        {
            _suppressSearch = false;
        }

        IsNetworkEnabled = document.NetworkConsentGrantedAtUtc.HasValue;
        _networkConsentGrantedAtUtc = document.NetworkConsentGrantedAtUtc;
        SelectedLocation = document.SelectedLocation;
        AutoRefreshMinutes = Math.Clamp(document.AutoRefreshMinutes, 5, 180);
        ColorfulTheme = document.ColorfulTheme;
        ShowLocation = document.ShowLocation; ShowIcon = document.ShowIcon; ShowTemperature = document.ShowTemperature;
        ShowCondition = document.ShowCondition; ShowDailyRange = document.ShowDailyRange; ShowSunrise = document.ShowSunrise;
        ShowSunset = document.ShowSunset; ShowForecast = document.ShowForecast;
        ShowWind = document.ShowWind;
        ShowUpdatedTime = document.ShowUpdatedTime;
        ClearSearchResults();
        ResetSnapshot();
        StatusText = document.SelectedLocation is null
            ? UiText.Get("ChooseALocationToFetchWeather")
            : UiText.Get("RefreshingWeather");
    }

    /// <summary>Zwraca stan wymagający zapisu.</summary>
    public WeatherPanelDocument Snapshot() => new()
    {
        ModulePositions = [.. ModulePositions],
        ModuleSpacing = ModuleSpacing,
        NetworkConsentGrantedAtUtc = _networkConsentGrantedAtUtc,
        SelectedLocation = SelectedLocation,
        AutoRefreshMinutes = AutoRefreshMinutes,
        ColorfulTheme = ColorfulTheme,
        ShowLocation = ShowLocation, ShowIcon = ShowIcon, ShowTemperature = ShowTemperature,
        ShowCondition = ShowCondition, ShowDailyRange = ShowDailyRange, ShowSunrise = ShowSunrise,
        ShowSunset = ShowSunset, ShowForecast = ShowForecast,
        ShowWind = ShowWind,
        ShowUpdatedTime = ShowUpdatedTime,
    };

    /// <summary>Potwierdza zapis konfiguracji panelu.</summary>
    public void MarkPersisted()
    {
    }

    /// <summary>Uruchamia pierwszy odczyt po odtworzeniu panelu.</summary>
    public Task ActivateAsync(CancellationToken cancellationToken) =>
        SelectedLocation is null ? Task.CompletedTask : ActivateCoreAsync(cancellationToken);

    /// <summary>Odświeża bieżący odczyt pogody.</summary>
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _networkCts.Cancel();
        _networkCts.Dispose();
        lock (_gate)
        {
            _searchCts?.Cancel();

            _searchCts = null;
            _autoRefreshCts?.Cancel();
            _autoRefreshCts?.Dispose();
            _autoRefreshCts = null;
        }
    }

    public IRelayCommand<string> OpenSourceCommand { get; }

    private void OpenSource(string? source)
    {
        var url = source switch
        {
            "weather" => "https://open-meteo.com/",
            "licence" => "https://open-meteo.com/en/licence",
            "geonames" => "https://www.geonames.org/",
            _ => null,
        };
        if (url is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { _notifier.Publish(UiText.Get("CouldNotOpenTheDataProviderS"), PanelNotificationSeverity.Warning); }
    }

    private bool CanRefresh() => IsNetworkEnabled && SelectedLocation is not null && !IsLoading;

    private async Task SelectLocationAsync(WeatherLocationOption? location)
    {
        if (_disposed || !IsNetworkEnabled || location is null)
        {
            return;
        }

        SelectedLocation = location;

        _suppressSearch = true;
        try
        {
            SearchText = location.DisplayName;
        }
        finally
        {
            _suppressSearch = false;
        }

        ClearSearchResults();
        Changed?.Invoke(this, EventArgs.Empty);
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void ClearLocation()
    {
        SelectedLocation = null;
        SearchText = string.Empty;
        ClearSearchResults();
        ResetSnapshot();
        IsStale = false;

        StatusText = UiText.Get("ChooseALocationToFetchWeather");
        ScheduleAutoRefresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SearchAsync(string rawQuery)
    {
        CancelSearch();

        if (_disposed || !IsNetworkEnabled || string.IsNullOrWhiteSpace(rawQuery))
        {
            ClearSearchResults();
            IsSearching = false;
            return;
        }

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _searchCts = cts;
        }

        try
        {
            IsSearching = true;
            await Task.Delay(SearchDebounceDelay, cts.Token).ConfigureAwait(true);
            var results = await _locationSearchService.SearchAsync(rawQuery.Trim(), 8, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            ReplaceSearchResults(results);
            StatusText = results.Count == 0
                ? UiText.Get("NoLocationsFound")
                : UiText.Get("ChooseALocationFromTheResults");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ClearSearchResults();
            StatusText = UiText.Get("CouldNotSearchForLocations");
            _notifier.Publish(UiText.Get("LocationSearchFailed"), PanelNotificationSeverity.Warning);
        }
        finally
        {
            IsSearching = false;
            lock (_gate)
            {
                if (ReferenceEquals(_searchCts, cts))
                {
                    _searchCts = null;
                }
            }

            cts.Dispose();
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var location = SelectedLocation;
        if (_disposed || !IsNetworkEnabled || location is null)
        {
            return;
        }

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkCts.Token);
        cancellationToken = requestCts.Token;
        if (IsLoading) return;
        IsLoading = true;
        IsStale = false;
        StatusText = UiText.Get("FetchingCurrentConditions");
        RefreshCommand.NotifyCanExecuteChanged();

        try
        {
            var snapshot = await _weatherService
                .GetCurrentAsync(location.Latitude, location.Longitude, cancellationToken)
                .ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || !IsNetworkEnabled || SelectedLocation != location) return;
            ApplySnapshot(snapshot);

            await _cacheStore
                .UpsertAsync(location.CacheKey, snapshot, snapshot.FetchedAtUtc.Add(DefaultCacheLifetime), cancellationToken)
                .ConfigureAwait(true);

            StatusText = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusText = UiText.Get("WeatherRequestCanceled");
        }
        catch (Exception)
        {
            var cached = await _cacheStore.GetAsync(location.CacheKey, cancellationToken).ConfigureAwait(true);
            if (cached is not null)
            {
                ApplySnapshot(cached.Snapshot with { IsFromCache = true });
                IsStale = true;
                StatusText = UiText.Format("NoNewDataShowingTheLatestCached", location.DisplayName);
                _notifier.Publish(UiText.Get("WeatherRefreshFailedShowingCachedData"), PanelNotificationSeverity.Warning);
            }
            else
            {
                ResetSnapshot();
                StatusText = UiText.Format("CouldNotFetchWeatherFor", location.DisplayName);
                _notifier.Publish(UiText.Get("WeatherRequestFailed"), PanelNotificationSeverity.Error);
            }
        }
        finally
        {
            IsLoading = false;
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private void ApplySnapshot(WeatherSnapshot snapshot)
    {
        TemperatureCelsius = snapshot.TemperatureCelsius;
        ConditionText = WeatherConditionCatalog.Describe(snapshot.WeatherCode);
        ConditionGlyph = WeatherConditionCatalog.Glyph(snapshot.WeatherCode, snapshot.IsDay);
        ColorConditionGlyph = WeatherConditionCatalog.ColorGlyph(snapshot.WeatherCode, snapshot.IsDay);
        WindText = $"{snapshot.WindSpeedKph:0.#} km/h";
        UpdatedText = snapshot.SourceTime.ToString("yyyy-MM-dd HH:mm zzz");
        DailyRangeText = snapshot.DailyHighCelsius.HasValue || snapshot.DailyLowCelsius.HasValue
            ? UiText.Format("HighLow2", FormatTemperature(snapshot.DailyHighCelsius), FormatTemperature(snapshot.DailyLowCelsius))
            : "—";
        SunriseText = snapshot.Sunrise?.ToString("HH:mm") ?? "—";
        SunsetText = snapshot.Sunset?.ToString("HH:mm") ?? "—";
        ReplaceHourlyForecast(snapshot.HourlyForecast ?? Array.Empty<WeatherHourlyForecastPoint>());
        IsStale = snapshot.IsFromCache;
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(HasDailyDetails));
    }

    private void ResetSnapshot()
    {
        TemperatureCelsius = null;
        ConditionText = UiText.Get("NoData");
        ConditionGlyph = "•";
        ColorConditionGlyph = "•";
        WindText = "—";
        UpdatedText = "—";
        DailyRangeText = "—";
        SunriseText = "—";
        SunsetText = "—";
        HourlyForecast.Clear();
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(HasDailyDetails));
        OnPropertyChanged(nameof(HasHourlyForecast));
    }

    private void ReplaceSearchResults(IReadOnlyList<WeatherLocationOption> results)
    {
        SearchResults.Clear();
        foreach (var result in results)
        {
            SearchResults.Add(result);
        }
    }

    private void ClearSearchResults()
    {
        if (SearchResults.Count == 0)
        {
            return;
        }

        SearchResults.Clear();
    }

    private void CancelSearch()
    {
        lock (_gate)
        {
            _searchCts?.Cancel();

            _searchCts = null;
        }
    }

    private void ReplaceHourlyForecast(IReadOnlyList<WeatherHourlyForecastPoint> forecast)
    {
        HourlyForecast.Clear();
        foreach (var point in forecast)
        {
            HourlyForecast.Add(new WeatherHourlyForecastDisplayItem(
                point.Time.ToString("HH:mm"),
                WeatherConditionCatalog.Glyph(point.WeatherCode, point.IsDay),
                WeatherConditionCatalog.ColorGlyph(point.WeatherCode, point.IsDay),
                $"{point.TemperatureCelsius:0.#} °C",
                WeatherConditionCatalog.Describe(point.WeatherCode), point.TemperatureCelsius));
        }

        OnPropertyChanged(nameof(HasHourlyForecast));
    }

    private static string FormatTemperature(double? value) =>
        value.HasValue ? $"{value.Value:0.#} °C" : "—";

    private async Task ActivateCoreAsync(CancellationToken cancellationToken)
    {
        ScheduleAutoRefresh();
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private void ScheduleAutoRefresh()
    {
        lock (_gate)
        {
            _autoRefreshCts?.Cancel();
            _autoRefreshCts?.Dispose();
            _autoRefreshCts = null;

            if (_disposed || !IsNetworkEnabled || SelectedLocation is null)
            {
                return;
            }

            var refreshMinutes = Math.Clamp(AutoRefreshMinutes, 5, 180);
            var cts = new CancellationTokenSource();
            _autoRefreshCts = cts;
            _ = RunAutoRefreshLoopAsync(TimeSpan.FromMinutes(refreshMinutes), cts.Token);
        }
    }

    private async Task RunAutoRefreshLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
