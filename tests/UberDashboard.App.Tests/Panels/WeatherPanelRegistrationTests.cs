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
    public async Task Built_in_catalog_contains_expected_panels()
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
        Assert.Equal(8, catalog.Registrations.Count);
        Assert.NotNull(catalog.DescriptorOf("shortcuts"));
        Assert.NotNull(catalog.DescriptorOf("calendar"));
        Assert.NotNull(catalog.DescriptorOf("system-resources"));
        Assert.NotNull(catalog.DescriptorOf("affirmations"));
        Assert.NotNull(catalog.DescriptorOf(UberDashboard.App.Features.Space.SpacePanelDescriptor.Id));
    }
}
