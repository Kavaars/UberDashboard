# Shell, Tryby Okna, Pogoda i Ustawienia Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dowozić brakujące funkcje v1 UberDashboard: trzy tryby okna, panel Weather, redesign shella oraz moduł Opcje/Ustawienia z personalizacją działającą na żywo.

**Architecture:** Implementacja idzie warstwami od fundamentów do UI: najpierw kontrakty i ustawienia trybów okna, potem koordynacja okna i platformy Windows, następnie infrastruktura i panel Weather, a na końcu nowy shell oraz ustawienia personalizacji. Logika ma zostać poza `MainWindow.xaml.cs`; code-behind tylko wiąże okno z koordynatorem i zdarzeniami WPF.

**Tech Stack:** .NET 10, WPF, xUnit, SQLite, existing `AppBarController`, existing `IAppSettingsStore`, Open-Meteo via `HttpClient`.

---

## File Structure

### Nowe lub rozszerzane jednostki

- `src/UberDashboard.Application/Platform/WindowMode.cs`
  - enum trybów okna
- `src/UberDashboard.Application/Platform/WindowModeSettings.cs`
  - ustawienia trybu startowego, monitora, krawędzi, grubości i widget opacity
- `src/UberDashboard.Application/Platform/IWindowModeSettingsService.cs`
  - odczyt/zapis ustawień trybów okna
- `src/UberDashboard.Application/Platform/IWindowModeCoordinator.cs`
  - interfejs koordynujący wejście i wyjście z trybów
- `src/UberDashboard.App/Shell/MainWindowModeCoordinator.cs`
  - implementacja powiązana z WPF window handle i usługami platformowymi
- `src/UberDashboard.App/Shell/DesktopWidgetWindowHost.cs`
  - helper `best effort` dla desktop widget
- `src/UberDashboard.App/Settings/*`
  - view modele i okno ustawień
- `src/UberDashboard.App/Features/Weather/*`
  - descriptor/factory/instance/view model
- `src/UberDashboard.Application/Weather/*`
  - modele i serwisy warstwy aplikacji
- `src/UberDashboard.Infrastructure/Weather/*`
  - klient HTTP i cache store
- `src/UberDashboard.App/Theming/PersonalizationTheme.cs`
  - tokeny personalizacji
- `src/UberDashboard.App/Theming/ThemeService.cs`
  - rozszerzenie o personalizację dynamicznych tokenów

### Istniejące pliki do modyfikacji

- `src/UberDashboard.App/MainWindow.xaml`
- `src/UberDashboard.App/MainWindow.xaml.cs`
- `src/UberDashboard.App/Shell/ShellViewModel.cs`
- `src/UberDashboard.App/Hosting/AppServiceCollectionExtensions.cs`
- `src/UberDashboard.App/Theming/Controls.xaml`
- `src/UberDashboard.App/Theming/PanelTemplates.xaml`
- `src/UberDashboard.App/App.xaml`
- `src/UberDashboard.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- `src/UberDashboard.Infrastructure/Database/Stores/AppSettingsStore.cs`

### Testy

- `tests/UberDashboard.Application.Tests/Platform/*`
- `tests/UberDashboard.Application.Tests/Panels/*`
- `tests/UberDashboard.Infrastructure.Tests/Database/*`
- nowe testy w:
  - `tests/UberDashboard.Application.Tests/Platform/WindowModeSettingsServiceTests.cs`
  - `tests/UberDashboard.Application.Tests/Platform/MainWindowModeCoordinatorTests.cs`
  - `tests/UberDashboard.Application.Tests/Weather/WeatherStateReducerTests.cs`
  - `tests/UberDashboard.Infrastructure.Tests/Weather/OpenMeteoWeatherServiceTests.cs`
  - `tests/UberDashboard.Infrastructure.Tests/Database/WeatherCacheStoreTests.cs`

## Task 1: Kontrakty trybów okna i ustawień

**Files:**
- Create: `src/UberDashboard.Application/Platform/WindowMode.cs`
- Create: `src/UberDashboard.Application/Platform/WindowModeSettings.cs`
- Create: `src/UberDashboard.Application/Platform/IWindowModeSettingsService.cs`
- Create: `tests/UberDashboard.Application.Tests/Platform/WindowModeSettingsContractTests.cs`
- Modify: `src/UberDashboard.Application/Platform/AppBarTypes.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.Platform;

public sealed class WindowModeSettingsContractTests
{
    [Fact]
    public void Defaults_use_normal_mode_and_safe_values()
    {
        var settings = WindowModeSettings.Default;

        Assert.Equal(WindowMode.Normal, settings.LaunchMode);
        Assert.Equal(AppBarEdge.Bottom, settings.DockEdge);
        Assert.True(settings.DockThickness >= 40);
        Assert.InRange(settings.WidgetOpacity, 0.2, 1.0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter WindowModeSettingsContractTests -v q`

Expected: FAIL, bo `WindowMode` i `WindowModeSettings` jeszcze nie istnieją.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace UberDashboard.Application.Platform;

public enum WindowMode
{
    Normal = 0,
    DesktopWidget = 1,
    DockedBar = 2,
}

public sealed record WindowModeSettings(
    WindowMode LaunchMode,
    string? MonitorId,
    AppBarEdge DockEdge,
    int DockThickness,
    double WidgetOpacity,
    bool DesktopWidgetFallbackToNormal)
{
    public static WindowModeSettings Default { get; } =
        new(WindowMode.Normal, null, AppBarEdge.Bottom, 56, 0.92d, true);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter WindowModeSettingsContractTests -v q`

Expected: PASS.

- [ ] **Step 5: Add settings service contract**

```csharp
namespace UberDashboard.Application.Platform;

public interface IWindowModeSettingsService
{
    Task<WindowModeSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SetAsync(WindowModeSettings settings, CancellationToken cancellationToken = default);
}
```

## Task 2: Serwis ustawień trybów okna na bazie `IAppSettingsStore`

**Files:**
- Create: `src/UberDashboard.Infrastructure/Settings/WindowModeSettingsService.cs`
- Create: `tests/UberDashboard.Infrastructure.Tests/Database/WindowModeSettingsServiceTests.cs`
- Modify: `src/UberDashboard.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using UberDashboard.Application.Platform;
using UberDashboard.Infrastructure.Database.Stores;
using UberDashboard.Infrastructure.Settings;
using UberDashboard.Infrastructure.Tests.TestSupport;

namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class WindowModeSettingsServiceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WindowModeSettingsServiceTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Persists_and_restores_window_mode_settings()
    {
        var service = _fixture.CreateWindowModeSettingsService();
        var input = new WindowModeSettings(WindowMode.DockedBar, "DISPLAY1", AppBarEdge.Left, 64, 0.75d, true);

        await service.SetAsync(input);
        var restored = await service.GetAsync();

        Assert.Equal(input, restored);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj" --filter WindowModeSettingsServiceTests -v q`

Expected: FAIL, bo serwis i helper testowy nie istnieją.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.Json;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Platform;

namespace UberDashboard.Infrastructure.Settings;

public sealed class WindowModeSettingsService : IWindowModeSettingsService
{
    private const string SettingsKey = "app.windowMode";
    private readonly IAppSettingsStore _store;
    private readonly IClock _clock;

    public WindowModeSettingsService(IAppSettingsStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<WindowModeSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _store.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(raw)
            ? WindowModeSettings.Default
            : JsonSerializer.Deserialize<WindowModeSettings>(raw) ?? WindowModeSettings.Default;
    }

    public Task SetAsync(WindowModeSettings settings, CancellationToken cancellationToken = default) =>
        _store.SetAsync(SettingsKey, JsonSerializer.Serialize(settings), _clock.UtcNow, cancellationToken);
}
```

- [ ] **Step 4: Register service in infrastructure**

```csharp
services.AddSingleton<IWindowModeSettingsService, WindowModeSettingsService>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test "tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj" --filter WindowModeSettingsServiceTests -v q`

Expected: PASS.

## Task 3: Koordynator trybów okna dla `Normal` i `DockedBar`

**Files:**
- Create: `src/UberDashboard.Application/Platform/IWindowModeCoordinator.cs`
- Create: `src/UberDashboard.App/Shell/MainWindowModeCoordinator.cs`
- Create: `tests/UberDashboard.Application.Tests/Platform/MainWindowModeCoordinatorTests.cs`
- Modify: `src/UberDashboard.App/MainWindow.xaml.cs`
- Modify: `src/UberDashboard.App/Hosting/AppServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.Platform;

public sealed class MainWindowModeCoordinatorTests
{
    [Fact]
    public void Enter_docked_bar_registers_appbar_and_updates_current_mode()
    {
        var api = new FakeAppBarNativeApi();
        var appBar = new AppBarController(api);
        var coordinator = MainWindowModeCoordinator.ForTests(appBar);

        var result = coordinator.EnterDockedBar(new IntPtr(0x1234), "DISPLAY1", AppBarEdge.Bottom, 56, new PixelRect(0, 0, 1920, 1080));

        Assert.True(result.IsSuccess);
        Assert.Equal(WindowMode.DockedBar, coordinator.CurrentMode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter MainWindowModeCoordinatorTests -v q`

Expected: FAIL, bo koordynator nie istnieje.

- [ ] **Step 3: Write minimal coordinator contract**

```csharp
namespace UberDashboard.Application.Platform;

public interface IWindowModeCoordinator
{
    WindowMode CurrentMode { get; }
    Task<WindowModeTransitionResult> SwitchAsync(WindowMode mode, CancellationToken cancellationToken = default);
}

public sealed record WindowModeTransitionResult(bool IsSuccess, WindowMode ActiveMode, string? Message)
{
    public static WindowModeTransitionResult Success(WindowMode mode) => new(true, mode, null);
    public static WindowModeTransitionResult Fail(WindowMode fallback, string message) => new(false, fallback, message);
}
```

- [ ] **Step 4: Implement minimal `MainWindowModeCoordinator`**

```csharp
public sealed class MainWindowModeCoordinator : IWindowModeCoordinator
{
    private readonly AppBarController _appBar;
    public WindowMode CurrentMode { get; private set; } = WindowMode.Normal;

    public MainWindowModeCoordinator(AppBarController appBar) => _appBar = appBar;

    internal static MainWindowModeCoordinator ForTests(AppBarController appBar) => new(appBar);

    internal AppBarDockResult EnterDockedBar(IntPtr handle, string? _, AppBarEdge edge, int thickness, PixelRect workArea)
    {
        var result = _appBar.Register(handle, edge, thickness, workArea);
        if (result.IsSuccess)
        {
            CurrentMode = WindowMode.DockedBar;
        }

        return result;
    }

    public Task<WindowModeTransitionResult> SwitchAsync(WindowMode mode, CancellationToken cancellationToken = default)
    {
        CurrentMode = mode;
        return Task.FromResult(WindowModeTransitionResult.Success(mode));
    }
}
```

- [ ] **Step 5: Wire the coordinator into `MainWindow.xaml.cs`**

```csharp
public MainWindow(ShellViewModel viewModel, ThemeService theme, IWindowModeCoordinator windowModeCoordinator)
{
    _viewModel = viewModel;
    _theme = theme;
    _windowModeCoordinator = windowModeCoordinator;
    InitializeComponent();
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter "MainWindowModeCoordinatorTests|AppBarControllerTests" -v q`

Expected: PASS.

## Task 4: `DesktopWidget` host z fallbackiem `best effort`

**Files:**
- Create: `src/UberDashboard.App/Shell/DesktopWidgetWindowHost.cs`
- Modify: `src/UberDashboard.App/Shell/MainWindowModeCoordinator.cs`
- Create: `tests/UberDashboard.Application.Tests/Platform/DesktopWidgetFallbackTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.Platform;

public sealed class DesktopWidgetFallbackTests
{
    [Fact]
    public async Task Fallbacks_to_normal_when_desktop_attach_fails()
    {
        var coordinator = MainWindowModeCoordinator.ForDesktopWidgetFailure();

        var result = await coordinator.SwitchAsync(WindowMode.DesktopWidget);

        Assert.False(result.IsSuccess);
        Assert.Equal(WindowMode.Normal, result.ActiveMode);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter DesktopWidgetFallbackTests -v q`

Expected: FAIL.

- [ ] **Step 3: Implement minimal widget host contract**

```csharp
namespace UberDashboard.App.Shell;

public interface IDesktopWidgetWindowHost
{
    Task<bool> TryAttachAsync(Window window, CancellationToken cancellationToken);
    Task DetachAsync(Window window, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Implement fallback logic in coordinator**

```csharp
if (mode == WindowMode.DesktopWidget)
{
    var attached = await _desktopWidgetHost.TryAttachAsync(_window, cancellationToken).ConfigureAwait(true);
    if (!attached)
    {
        CurrentMode = WindowMode.Normal;
        return WindowModeTransitionResult.Fail(WindowMode.Normal, "Nie udało się aktywować trybu widgetu pulpitu.");
    }

    CurrentMode = WindowMode.DesktopWidget;
    return WindowModeTransitionResult.Success(WindowMode.DesktopWidget);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter "DesktopWidgetFallbackTests|MainWindowModeCoordinatorTests" -v q`

Expected: PASS.

## Task 5: Warstwa aplikacyjna i infrastrukturalna `Weather`

**Files:**
- Create: `src/UberDashboard.Application/Weather/WeatherLocationOption.cs`
- Create: `src/UberDashboard.Application/Weather/WeatherSnapshot.cs`
- Create: `src/UberDashboard.Application/Weather/IWeatherService.cs`
- Create: `src/UberDashboard.Application/Weather/IWeatherLocationSearchService.cs`
- Create: `src/UberDashboard.Application/Weather/IWeatherCacheStore.cs`
- Create: `src/UberDashboard.Infrastructure/Weather/OpenMeteoWeatherService.cs`
- Create: `src/UberDashboard.Infrastructure/Weather/OpenMeteoLocationSearchService.cs`
- Create: `src/UberDashboard.Infrastructure/Weather/WeatherCacheStore.cs`
- Create: `tests/UberDashboard.Infrastructure.Tests/Weather/OpenMeteoWeatherServiceTests.cs`
- Create: `tests/UberDashboard.Infrastructure.Tests/Database/WeatherCacheStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
namespace UberDashboard.Infrastructure.Tests.Weather;

public sealed class OpenMeteoWeatherServiceTests
{
    [Fact]
    public async Task Returns_snapshot_from_forecast_response()
    {
        var service = WeatherTestFactory.CreateWeatherService("""
        {"current":{"temperature_2m":17.4,"weather_code":3,"wind_speed_10m":12.6,"time":"2026-11-02T08:00"}}
        """);

        var snapshot = await service.GetCurrentAsync(50.0614, 19.9366, CancellationToken.None);

        Assert.Equal(17.4d, snapshot.TemperatureCelsius);
        Assert.Equal(3, snapshot.WeatherCode);
    }
}
```

```csharp
namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class WeatherCacheStoreTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Stores_and_restores_latest_snapshot()
    {
        var store = _fixture.CreateWeatherCacheStore();
        await store.UpsertAsync("50.06,19.93", """{"temperature":17.4}""", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(30));
        var cached = await store.GetAsync("50.06,19.93");
        Assert.NotNull(cached);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test "tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj" --filter "OpenMeteoWeatherServiceTests|WeatherCacheStoreTests" -v q`

Expected: FAIL.

- [ ] **Step 3: Implement minimal contracts and DTOs**

```csharp
public sealed record WeatherLocationOption(string Name, string? Region, string Country, double Latitude, double Longitude);
public sealed record WeatherSnapshot(double TemperatureCelsius, int WeatherCode, double WindSpeedKph, DateTimeOffset SourceTime, DateTimeOffset FetchedAtUtc, bool IsFromCache);
```

- [ ] **Step 4: Implement HTTP services and cache store**

```csharp
public sealed class OpenMeteoWeatherService : IWeatherService
{
    public async Task<WeatherSnapshot> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync($"/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code,wind_speed_10m", cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OpenMeteoForecastResponse>(cancellationToken: cancellationToken);
        return new WeatherSnapshot(payload!.Current.Temperature2m, payload.Current.WeatherCode, payload.Current.WindSpeed10m, payload.Current.Time, _clock.UtcNow, false);
    }
}
```

- [ ] **Step 5: Register services**

```csharp
services.AddHttpClient<IWeatherService, OpenMeteoWeatherService>(client =>
{
    client.BaseAddress = new Uri("https://api.open-meteo.com");
    client.Timeout = TimeSpan.FromSeconds(10);
});

services.AddHttpClient<IWeatherLocationSearchService, OpenMeteoLocationSearchService>(client =>
{
    client.BaseAddress = new Uri("https://geocoding-api.open-meteo.com");
    client.Timeout = TimeSpan.FromSeconds(10);
});

services.AddSingleton<IWeatherCacheStore, WeatherCacheStore>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test "tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj" --filter "OpenMeteoWeatherServiceTests|WeatherCacheStoreTests" -v q`

Expected: PASS.

## Task 6: Panel `Weather` jako trzeci built-in panel

**Files:**
- Create: `src/UberDashboard.App/Features/Weather/WeatherPanelDescriptor.cs`
- Create: `src/UberDashboard.App/Features/Weather/WeatherPanelFactory.cs`
- Create: `src/UberDashboard.App/Features/Weather/WeatherPanelInstance.cs`
- Create: `src/UberDashboard.App/Features/Weather/WeatherPanelViewModel.cs`
- Modify: `src/UberDashboard.App/Hosting/AppServiceCollectionExtensions.cs`
- Modify: `src/UberDashboard.App/Theming/PanelTemplates.xaml`
- Create: `tests/UberDashboard.Application.Tests/Panels/WeatherPanelRegistrationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.App.Hosting;
using UberDashboard.Application.Panels;

namespace UberDashboard.Application.Tests.Panels;

public sealed class WeatherPanelRegistrationTests
{
    [Fact]
    public void Built_in_catalog_contains_weather_panel()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUberDashboardApp();
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<PanelCatalog>();

        Assert.NotNull(catalog.DescriptorOf("weather"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter WeatherPanelRegistrationTests -v q`

Expected: FAIL.

- [ ] **Step 3: Add descriptor and registration**

```csharp
public sealed class WeatherPanelDescriptor : IPanelDescriptor
{
    public const string TypeId = "weather";
    public string DisplayName => "Pogoda";
    public string? IconKey => "panel.weather";
    public MinSize MinSize => new(320d, 220d);
    public int StateSchemaVersion => 1;
    public IPanelFactory Factory { get; }
}
```

```csharp
services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
    WeatherPanelDescriptor.TypeId,
    new WeatherPanelDescriptor(new WeatherPanelFactory(
        provider.GetRequiredService<PanelStateScheduler>(),
        provider.GetRequiredService<IWeatherService>(),
        provider.GetRequiredService<IWeatherLocationSearchService>(),
        provider.GetRequiredService<IWeatherCacheStore>()))));
```

- [ ] **Step 4: Implement minimal panel state flow**

```csharp
public sealed class WeatherPanelViewModel : ObservableObject
{
    [ObservableProperty] private string title = "Wybierz lokalizację";
    [ObservableProperty] private double? temperature;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string status = "Brak danych pogodowych.";
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter WeatherPanelRegistrationTests -v q`

Expected: PASS.

## Task 7: Ustawienia aplikacji i personalizacja live

**Files:**
- Create: `src/UberDashboard.App/Settings/ApplicationSettings.cs`
- Create: `src/UberDashboard.App/Settings/IApplicationSettingsService.cs`
- Create: `src/UberDashboard.App/Settings/ApplicationSettingsService.cs`
- Create: `src/UberDashboard.App/Settings/SettingsWindow.xaml`
- Create: `src/UberDashboard.App/Settings/SettingsWindow.xaml.cs`
- Create: `src/UberDashboard.App/Settings/SettingsViewModel.cs`
- Create: `tests/UberDashboard.Infrastructure.Tests/Database/ApplicationSettingsServiceTests.cs`
- Modify: `src/UberDashboard.App/Theming/ThemeService.cs`
- Modify: `src/UberDashboard.App/App.xaml`

- [ ] **Step 1: Write the failing test**

```csharp
namespace UberDashboard.Infrastructure.Tests.Database;

public sealed class ApplicationSettingsServiceTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Persists_personalization_settings()
    {
        var service = _fixture.CreateApplicationSettingsService();
        var input = new ApplicationSettings
        {
            Theme = "Dark",
            AccentColor = "#4F7CFF",
            FontFamily = "Segoe UI",
            FontSize = 15,
            CornerRadius = 12,
        };

        await service.SaveAsync(input);
        var restored = await service.LoadAsync();

        Assert.Equal(input.AccentColor, restored.AccentColor);
        Assert.Equal(input.FontSize, restored.FontSize);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj" --filter ApplicationSettingsServiceTests -v q`

Expected: FAIL.

- [ ] **Step 3: Implement model and service**

```csharp
public sealed class ApplicationSettings
{
    public string Theme { get; set; } = "System";
    public string AccentColor { get; set; } = "#4F7CFF";
    public string SurfaceColor { get; set; } = "#F4F7FB";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 14d;
    public double CornerRadius { get; set; } = 12d;
    public double UiOpacity { get; set; } = 1d;
}
```

- [ ] **Step 4: Extend `ThemeService` to apply tokens live**

```csharp
application.Resources["Token.Accent"] = (Color)ColorConverter.ConvertFromString(settings.AccentColor);
application.Resources["Token.FontFamily"] = new FontFamily(settings.FontFamily);
application.Resources["Token.FontSize.Body"] = settings.FontSize;
application.Resources["Token.CornerRadius.M"] = new CornerRadius(settings.CornerRadius);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test "tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj" --filter ApplicationSettingsServiceTests -v q`

Expected: PASS.

## Task 8: Nowy shell UI i okno ustawień

**Files:**
- Modify: `src/UberDashboard.App/MainWindow.xaml`
- Modify: `src/UberDashboard.App/MainWindow.xaml.cs`
- Modify: `src/UberDashboard.App/Shell/ShellViewModel.cs`
- Modify: `src/UberDashboard.App/Theming/Controls.xaml`
- Modify: `src/UberDashboard.App/Theming/PanelTemplates.xaml`
- Create: `src/UberDashboard.App/Theming/VisualTokens.xaml`

- [ ] **Step 1: Write the failing test for shell state**

```csharp
namespace UberDashboard.Application.Tests.Panels;

public sealed class ShellViewModelCommandsTests
{
    [Fact]
    public void Exposes_settings_and_window_mode_commands()
    {
        var vm = ShellViewModelFactory.Create();

        Assert.NotNull(vm.OpenSettingsCommand);
        Assert.NotNull(vm.SwitchWindowModeCommand);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter ShellViewModelCommandsTests -v q`

Expected: FAIL.

- [ ] **Step 3: Add commands and topbar bindings**

```csharp
public IAsyncRelayCommand OpenSettingsCommand { get; }
public IAsyncRelayCommand<WindowMode> SwitchWindowModeCommand { get; }
```

```xml
<StackPanel Orientation="Horizontal">
  <TextBlock Text="UberDashboard" />
  <ComboBox ItemsSource="{Binding Dashboards}" />
  <UniformGrid Rows="1" Columns="3">
    <Button Command="{Binding SwitchWindowModeCommand}" CommandParameter="{x:Static platform:WindowMode.Normal}" Content="Okno" />
    <Button Command="{Binding SwitchWindowModeCommand}" CommandParameter="{x:Static platform:WindowMode.DesktopWidget}" Content="Widget" />
    <Button Command="{Binding SwitchWindowModeCommand}" CommandParameter="{x:Static platform:WindowMode.DockedBar}" Content="Pasek" />
  </UniformGrid>
  <Button Command="{Binding OpenSettingsCommand}" Content="Opcje" />
</StackPanel>
```

- [ ] **Step 4: Apply the new visual system**

```xml
<Style x:Key="PanelCard" TargetType="Border">
  <Setter Property="CornerRadius" Value="{DynamicResource Token.CornerRadius.M}" />
  <Setter Property="Background" Value="{DynamicResource Brush.Surface}" />
  <Setter Property="BorderBrush" Value="{DynamicResource Brush.Border}" />
  <Setter Property="BorderThickness" Value="1" />
</Style>
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" --filter "ShellViewModelCommandsTests|WeatherPanelRegistrationTests" -v q`

Expected: PASS.

## Task 9: Pełna walidacja i dokumentacja projektu

**Files:**
- Modify: `f:\MyApps\UberDashboard\problemy.md`
- Modify: `f:\MyApps\UberDashboard\listaZmian.md`
- Modify: `f:\MyApps\UberDashboard\wersje.md`
- Modify: `f:\MyApps\UberDashboard\changelog.txt`

- [ ] **Step 1: Run targeted tests after each slice**

Run:

```powershell
dotnet test "tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj" -v q
dotnet test "tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj" -v q
```

Expected: PASS.

- [ ] **Step 2: Run full verification**

Run:

```powershell
dotnet build "UberDashboard.slnx" -v q
dotnet test "UberDashboard.slnx" -v q
```

Expected: PASS.

- [ ] **Step 3: Update issue log with confirmed cause and regression coverage**

```markdown
- Potwierdzona przyczyna:
  - brak implementacji feature'ów `Weather`, `WindowModes` i `Settings`
- Fix:
  - dodano panel `Weather`, koordynator trybów okna, ustawienia i nowy shell UI
- Test regresji:
  - `WindowModeSettingsServiceTests`
  - `MainWindowModeCoordinatorTests`
  - `WeatherPanelRegistrationTests`
```

- [ ] **Step 4: Update history files**

```markdown
- `listaZmian.md`: krótki wpis o trybach okna, pogodzie i ustawieniach
- `wersje.md`: nowa wersja po wdrożeniu
- `changelog.txt`: user-visible summary zmian
```

## Self-Review

- Spec coverage:
  - `WindowModes` -> Tasks 1-4
  - `Weather` -> Tasks 5-6
  - `Shell/UI Refresh` -> Task 8
  - `Opcje/Ustawienia` -> Tasks 7-8
  - dokumentacja i log problemu -> Task 9
- Placeholder scan:
  - brak `TODO`, `TBD` i odwołań typu „similar to task N”
- Type consistency:
  - `WindowMode`, `WindowModeSettings`, `IWindowModeCoordinator` i `ApplicationSettings` są nazwane spójnie w całym planie

## Notes

- Plan jest fazowy, ale celowo pozostaje jednym dokumentem, bo shell, tryby okna i ustawienia współdzielą te same miejsca integracji.
- Kroki commitów są pominięte, ponieważ bieżący workspace nie jest repozytorium git; po inicjalizacji repo można dodać commity per task bez zmiany kolejności wdrożenia.
