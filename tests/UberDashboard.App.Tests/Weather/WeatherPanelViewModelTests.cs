using UberDashboard.App.Features.Weather;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.Application.Weather;

namespace UberDashboard.App.Tests.Weather;

public sealed class WeatherPanelViewModelTests
{
    [Fact]
    public void Legacy_weather_modules_are_combined_without_losing_saved_positions()
    {
        using var vm = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        vm.Load(new WeatherPanelDocument { ModulePositions = [new("sunrise", 2, 3), new("sunset", 1, 2), new("condition", 0, 2)] });
        Assert.DoesNotContain(vm.Modules, m => m.Id is "sunset" or "condition");
        Assert.Equal((2, 3), (vm.ModulePositions.Single(p => p.Id == "sunrise").Column, vm.ModulePositions.Single(p => p.Id == "sunrise").Row));
        Assert.Contains(vm.Modules, m => m.Id == "forecast");
    }

    [Fact]
    public void Legacy_sunset_position_is_used_when_sunrise_position_is_missing()
    {
        using var vm = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        vm.Load(new WeatherPanelDocument { ModulePositions = [new("sunset", 3, 2)] });
        var position = vm.ModulePositions.Single(p => p.Id == "sunrise");
        Assert.Equal((3, 2), (position.Column, position.Row));
    }

    [Fact]
    public void Module_spacing_round_trips_and_old_state_defaults_to_zero()
    {
        using var vm = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        vm.Load(new WeatherPanelDocument());
        Assert.Equal(0, vm.ModuleSpacing);
        vm.ModuleSpacing = -8;
        using var restored = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        restored.Load(System.Text.Json.JsonSerializer.Deserialize<WeatherPanelDocument>(System.Text.Json.JsonSerializer.Serialize(vm.Snapshot()))!);
        Assert.Equal(-8, restored.ModuleSpacing);
    }

    [Fact]
    public void Modular_positions_preserve_empty_cells_and_round_trip()
    {
        using var vm = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        var initial = vm.ModulePositions.Single(position => position.Id == "icon");
        vm.MoveModuleToCell("icon", 3, 5);
        Assert.Equal(initial, vm.ModulePositions.Single(position => position.Id == "icon"));
        vm.IsModuleEditMode = true;
        vm.MoveModuleToCell("icon", 3, 5);
        using var restored = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        restored.Load(System.Text.Json.JsonSerializer.Deserialize<WeatherPanelDocument>(System.Text.Json.JsonSerializer.Serialize(vm.Snapshot()))!);
        var position = restored.ModulePositions.Single(item => item.Id == "icon");
        Assert.Equal((3, 5), (position.Column, position.Row));
        Assert.False(restored.IsModuleEditMode);
    }

    [Fact]
    public void Modular_collision_moves_the_existing_weather_element()
    {
        using var vm = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        vm.IsModuleEditMode = true;
        var location = vm.ModulePositions.Single(p => p.Id == "location");
        vm.MoveModuleToCell("icon", location.Column, location.Row);
        Assert.Equal((location.Column, location.Row), (vm.ModulePositions.Single(p => p.Id == "icon").Column, vm.ModulePositions.Single(p => p.Id == "icon").Row));
        Assert.Equal((1, 0), (vm.ModulePositions.Single(p => p.Id == "location").Column, vm.ModulePositions.Single(p => p.Id == "location").Row));
    }

    [Fact]
    public void Weather_visibility_options_round_trip_and_legacy_defaults_are_visible()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<WeatherPanelDocument>("{}");
        Assert.NotNull(legacy);
        Assert.True(legacy.ShowIcon && legacy.ShowForecast && legacy.ShowLocation);
        using var vm = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        vm.Load(legacy);
        vm.ShowLocation = false; vm.ShowIcon = false; vm.ShowTemperature = false; vm.ShowCondition = false;
        vm.ShowDailyRange = false; vm.ShowSunrise = false; vm.ShowSunset = false; vm.ShowForecast = false;
        var restored = System.Text.Json.JsonSerializer.Deserialize<WeatherPanelDocument>(System.Text.Json.JsonSerializer.Serialize(vm.Snapshot()));
        Assert.NotNull(restored);
        Assert.False(restored.ShowLocation || restored.ShowIcon || restored.ShowTemperature || restored.ShowCondition || restored.ShowDailyRange || restored.ShowSunrise || restored.ShowSunset || restored.ShowForecast);
    }
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
                Assert.Equal("☁", hour.Glyph);
                Assert.Equal("⛅", hour.ColorGlyph);
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
        source.ColorfulTheme = true;
        var saved = source.Snapshot();
        using var restored = new WeatherPanelViewModel(new FakeWeatherService(), new FakeWeatherLocationSearchService(), new FakeWeatherCacheStore(), new FakePanelNotifier());
        restored.Load(saved);
        Assert.True(restored.IsNetworkEnabled);
        Assert.True(restored.ColorfulTheme);
        Assert.Equal(saved.NetworkConsentGrantedAtUtc, restored.Snapshot().NetworkConsentGrantedAtUtc);
    }
    [Fact]
    public void Application_theme_glyphs_do_not_use_emoji_presentation()
    {
        foreach (var code in new[] { 0, 1, 2, 3, 45, 61, 71, 95 })
            Assert.DoesNotContain("⛅", WeatherConditionCatalog.Glyph(code, true));
    }
}
