using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UberDashboard.App.Hosting;
using UberDashboard.App.Tests.TestSupport;
using UberDashboard.Application.Panels;
using UberDashboard.Application.Weather;

namespace UberDashboard.App.Tests.Panels;

public sealed class WeatherPanelRegistrationTests
{
    [Fact]
    public async Task Built_in_catalog_contains_weather_panel()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IWeatherService, FakeWeatherService>();
        services.AddSingleton<IWeatherLocationSearchService, FakeWeatherLocationSearchService>();
        services.AddSingleton<IWeatherCacheStore, FakeWeatherCacheStore>();
        services.AddUberDashboardApp();

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<PanelCatalog>();

        Assert.NotNull(catalog.DescriptorOf("weather"));
        Assert.Equal(4, catalog.Registrations.Count);
        Assert.NotNull(catalog.DescriptorOf(UberDashboard.App.Features.Space.SpacePanelDescriptor.Id));
    }
}
