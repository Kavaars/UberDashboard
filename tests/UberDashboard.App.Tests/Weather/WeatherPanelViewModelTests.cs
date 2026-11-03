using UberDashboard.App.Features.Weather;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.Application.Weather;

namespace UberDashboard.App.Tests.Weather;

public sealed class WeatherPanelViewModelTests
{
    [Fact]
    public async Task Legacy_location_without_explicit_consent_does_not_send_requests()
    {
        var service = new FakeWeatherService();
        using var vm = new WeatherPanelViewModel(service, new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        vm.Load(new WeatherPanelDocument { SelectedLocation = new WeatherLocationOption("Test", "", "", 50, 20, "Europe/Warsaw") });
        await vm.ActivateAsync(CancellationToken.None);
        await vm.RefreshAsync();
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task Refresh_without_location_skips_weather_request()
    {
        var weatherService = new FakeWeatherService();
        using var viewModel = new WeatherPanelViewModel(
            weatherService,
            new FakeWeatherLocationSearchService(),
            new FakeWeatherCacheStore(),
            new FakePanelNotifier());

        await viewModel.RefreshAsync();

        Assert.Equal(0, weatherService.CallCount);
        Assert.False(viewModel.HasSnapshot);
        Assert.Equal("Wybierz lokalizację, aby pobrać pogodę.", viewModel.StatusText);
    }

    [Fact]
    public async Task Refresh_uses_cache_when_live_request_fails()
    {
        var location = new WeatherLocationOption("Kraków", "Małopolskie", "Polska", 50.0614d, 19.9366d, "Europe/Warsaw");
        var cachedSnapshot = new WeatherSnapshot(
            17.4d,
            3,
            12.6d,
            true,
            DateTimeOffset.Parse("2026-11-02T08:00:00+01:00"),
            DateTimeOffset.Parse("2026-11-02T08:05:00+00:00"),
            true);

        var weatherService = new FakeWeatherService
        {
            OnGetCurrentAsync = static (_, _, _) => Task.FromException<WeatherSnapshot>(new HttpRequestException("offline")),
        };

        var cacheStore = new FakeWeatherCacheStore();
        cacheStore.Seed(location.CacheKey, cachedSnapshot, cachedSnapshot.FetchedAtUtc.AddMinutes(20));

        using var viewModel = new WeatherPanelViewModel(
            weatherService,
            new FakeWeatherLocationSearchService(),
            cacheStore,
            new FakePanelNotifier())
        {
            IsNetworkEnabled = true,
            SelectedLocation = location,
        };

        await viewModel.RefreshAsync();

        Assert.Equal(1, weatherService.CallCount);
        Assert.Equal(1, cacheStore.GetCount);
        Assert.True(viewModel.IsStale);
        Assert.Equal(17.4d, viewModel.TemperatureCelsius);
        Assert.Equal("Pochmurno", viewModel.ConditionText);
    }

    [Fact]
    public async Task Refresh_populates_extended_daily_and_hourly_weather_data()
    {
        var location = new WeatherLocationOption("Poznań", "Wielkopolskie", "Polska", 52.4064d, 16.9252d, "Europe/Warsaw");
        var weatherService = new FakeWeatherService
        {
            OnGetCurrentAsync = static (_, _, _) => Task.FromResult(
                new WeatherSnapshot(
                    8.3d,
                    3,
                    4.2d,
                    true,
                    DateTimeOffset.Parse("2026-11-02T10:45:00+01:00"),
                    DateTimeOffset.Parse("2026-11-02T09:45:00+00:00"),
                    false,
                    11.8d,
                    4.1d,
                    DateTimeOffset.Parse("2026-11-02T06:38:00+01:00"),
                    DateTimeOffset.Parse("2026-11-02T16:21:00+01:00"),
                    [
                        new WeatherHourlyForecastPoint(DateTimeOffset.Parse("2026-11-02T11:00:00+01:00"), 8.7d, 3, true),
                        new WeatherHourlyForecastPoint(DateTimeOffset.Parse("2026-11-02T12:00:00+01:00"), 9.4d, 2, true),
                        new WeatherHourlyForecastPoint(DateTimeOffset.Parse("2026-11-02T13:00:00+01:00"), 10.1d, 1, true),
                    ])),
        };

        using var viewModel = new WeatherPanelViewModel(
            weatherService,
            new FakeWeatherLocationSearchService(),
            new FakeWeatherCacheStore(),
            new FakePanelNotifier())
        {
            IsNetworkEnabled = true,
            SelectedLocation = location,
        };

        await viewModel.RefreshAsync();

        Assert.Equal("Max: 11,8 °C | Min: 4,1 °C", viewModel.DailyRangeText);
        Assert.Equal("06:38", viewModel.SunriseText);
        Assert.Equal("16:21", viewModel.SunsetText);
        Assert.Equal(3, viewModel.HourlyForecast.Count);
        Assert.Collection(
            viewModel.HourlyForecast,
            hour =>
            {
                Assert.Equal("11:00", hour.TimeText);
                Assert.Equal("☁", hour.Glyph);
                Assert.Equal("8,7 °C", hour.TemperatureText);
            },
            hour =>
            {
                Assert.Equal("12:00", hour.TimeText);
                Assert.Equal("⛅", hour.Glyph);
                Assert.Equal("9,4 °C", hour.TemperatureText);
            },
            hour =>
            {
                Assert.Equal("13:00", hour.TimeText);
                Assert.Equal("☀", hour.Glyph);
                Assert.Equal("10,1 °C", hour.TemperatureText);
            });
    }
    [Fact]
    public async Task Revoking_consent_cancels_inflight_weather_and_clears_saved_consent()
    {
        var waiting = new TaskCompletionSource<WeatherSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var weather = new FakeWeatherService { OnGetCurrentAsync = (_, _, token) => waiting.Task.WaitAsync(token) };
        using var vm = new WeatherPanelViewModel(weather, new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier())
        {
            IsNetworkEnabled = true,
            SelectedLocation = new WeatherLocationOption("Test", "", "", 50, 20, "Europe/Warsaw"),
        };
        Assert.NotNull(vm.Snapshot().NetworkConsentGrantedAtUtc);
        var request = vm.RefreshAsync();
        Assert.Equal(1, weather.CallCount);
        vm.IsNetworkEnabled = false;
        await request;
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasSnapshot);
        Assert.Null(vm.Snapshot().NetworkConsentGrantedAtUtc);
        await vm.RefreshAsync();
        Assert.Equal(1, weather.CallCount);
    }

    [Fact]
    public void Explicit_consent_survives_document_roundtrip()
    {
        using var source = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        source.IsNetworkEnabled = true;
        var saved = source.Snapshot();
        using var restored = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        restored.Load(saved);
        Assert.True(restored.IsNetworkEnabled);
        Assert.Equal(saved.NetworkConsentGrantedAtUtc, restored.Snapshot().NetworkConsentGrantedAtUtc);
    }
}
