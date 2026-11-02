using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UberDashboard.App.Panels;
using UberDashboard.Application.Weather;
using UberDashboard.Contracts.Host;

namespace UberDashboard.App.Features.Weather;

/// <summary>Gotowy do renderowania wpis prognozy godzinowej.</summary>
public sealed record WeatherHourlyForecastDisplayItem(
    string TimeText,
    string Glyph,
    string TemperatureText,
    string ConditionText);

/// <summary>
/// Model widoku panelu pogody z wyszukiwaniem lokalizacji, odświeżaniem i fallbackiem do cache.
/// </summary>
public sealed partial class WeatherPanelViewModel : ObservableObject, IDisposable, IPanelOptionsPresenter
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromMinutes(20);

    private readonly IWeatherService _weatherService;
    private readonly IWeatherLocationSearchService _locationSearchService;
    private readonly IWeatherCacheStore _cacheStore;
    private readonly IPanelNotifier _notifier;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _autoRefreshCts;
    private bool _disposed;
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

        SearchResults = [];
        HourlyForecast = [];
        RefreshCommand = new AsyncRelayCommand(
            execute: () => RefreshAsync(CancellationToken.None),
            canExecute: CanRefresh);
        SelectLocationCommand = new AsyncRelayCommand<WeatherLocationOption>(SelectLocationAsync, location => location is not null);
        ClearLocationCommand = new RelayCommand(ClearLocation, () => SelectedLocation is not null);
        ToggleOptionsCommand = new RelayCommand(() => IsOptionsOpen = !IsOptionsOpen);

        StatusText = "Wybierz lokalizację, aby pobrać pogodę.";
        Title = "Pogoda";
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
    private string conditionText = "Brak danych";

    [ObservableProperty]
    private string conditionGlyph = "•";

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
    private int autoRefreshMinutes = 20;

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
        Title = value is null ? "Pogoda" : $"Pogoda · {value.Name}";
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

    partial void OnShowWindChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    partial void OnShowUpdatedTimeChanged(bool value) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Wczytuje zapisany stan panelu.</summary>
    public void Load(WeatherPanelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _suppressSearch = true;
        try
        {
            SearchText = document.SelectedLocation?.DisplayName ?? string.Empty;
        }
        finally
        {
            _suppressSearch = false;
        }

        SelectedLocation = document.SelectedLocation;
        AutoRefreshMinutes = Math.Clamp(document.AutoRefreshMinutes, 5, 180);
        ShowWind = document.ShowWind;
        ShowUpdatedTime = document.ShowUpdatedTime;
        ClearSearchResults();
        ResetSnapshot();
        StatusText = document.SelectedLocation is null
            ? "Wybierz lokalizację, aby pobrać pogodę."
            : "Odświeżam dane pogodowe…";
    }

    /// <summary>Zwraca stan wymagający zapisu.</summary>
    public WeatherPanelDocument Snapshot() => new()
    {
        SelectedLocation = SelectedLocation,
        AutoRefreshMinutes = AutoRefreshMinutes,
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
        lock (_gate)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
            _autoRefreshCts?.Cancel();
            _autoRefreshCts?.Dispose();
            _autoRefreshCts = null;
        }
    }

    private bool CanRefresh() => SelectedLocation is not null && !IsLoading;

    private async Task SelectLocationAsync(WeatherLocationOption? location)
    {
        if (location is null)
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
        StatusText = "Wybierz lokalizację, aby pobrać pogodę.";
        ScheduleAutoRefresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SearchAsync(string rawQuery)
    {
        CancelSearch();

        if (string.IsNullOrWhiteSpace(rawQuery))
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
                ? "Nie znaleziono lokalizacji."
                : "Wybierz lokalizację z listy wyników.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ClearSearchResults();
            StatusText = "Nie udało się wyszukać lokalizacji.";
            _notifier.Publish($"Wyszukiwanie lokalizacji nie powiodło się: {ex.Message}", PanelNotificationSeverity.Warning);
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
        if (location is null)
        {
            return;
        }

        IsLoading = true;
        IsStale = false;
        StatusText = "Pobieram bieżące warunki…";
        RefreshCommand.NotifyCanExecuteChanged();

        try
        {
            var snapshot = await _weatherService
                .GetCurrentAsync(location.Latitude, location.Longitude, cancellationToken)
                .ConfigureAwait(true);

            ApplySnapshot(snapshot);

            await _cacheStore
                .UpsertAsync(location.CacheKey, snapshot, snapshot.FetchedAtUtc.Add(DefaultCacheLifetime), cancellationToken)
                .ConfigureAwait(true);

            StatusText = $"Zaktualizowano dla {location.DisplayName}.";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var cached = await _cacheStore.GetAsync(location.CacheKey, cancellationToken).ConfigureAwait(true);
            if (cached is not null)
            {
                ApplySnapshot(cached.Snapshot with { IsFromCache = true });
                IsStale = true;
                StatusText = $"Brak świeżych danych. Pokazuję ostatni cache dla {location.DisplayName}.";
                _notifier.Publish($"Odświeżenie pogody nie powiodło się, używam cache: {ex.Message}", PanelNotificationSeverity.Warning);
            }
            else
            {
                ResetSnapshot();
                StatusText = $"Nie udało się pobrać pogody dla {location.DisplayName}.";
                _notifier.Publish($"Pobranie pogody nie powiodło się: {ex.Message}", PanelNotificationSeverity.Error);
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
        WindText = $"{snapshot.WindSpeedKph:0.#} km/h";
        UpdatedText = snapshot.SourceTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        DailyRangeText = snapshot.DailyHighCelsius.HasValue || snapshot.DailyLowCelsius.HasValue
            ? $"Max: {FormatTemperature(snapshot.DailyHighCelsius)} | Min: {FormatTemperature(snapshot.DailyLowCelsius)}"
            : "—";
        SunriseText = snapshot.Sunrise?.LocalDateTime.ToString("HH:mm") ?? "—";
        SunsetText = snapshot.Sunset?.LocalDateTime.ToString("HH:mm") ?? "—";
        ReplaceHourlyForecast(snapshot.HourlyForecast ?? Array.Empty<WeatherHourlyForecastPoint>());
        IsStale = snapshot.IsFromCache;
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(HasDailyDetails));
    }

    private void ResetSnapshot()
    {
        TemperatureCelsius = null;
        ConditionText = "Brak danych";
        ConditionGlyph = "•";
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
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    private void ReplaceHourlyForecast(IReadOnlyList<WeatherHourlyForecastPoint> forecast)
    {
        HourlyForecast.Clear();
        foreach (var point in forecast)
        {
            HourlyForecast.Add(new WeatherHourlyForecastDisplayItem(
                point.Time.LocalDateTime.ToString("HH:mm"),
                WeatherConditionCatalog.Glyph(point.WeatherCode, point.IsDay),
                $"{point.TemperatureCelsius:0.#} °C",
                WeatherConditionCatalog.Describe(point.WeatherCode)));
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

            if (_disposed || SelectedLocation is null)
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
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
