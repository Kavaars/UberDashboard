using UberDashboard.App.Panels;
using UberDashboard.Application.Weather;
using UberDashboard.Contracts.Panels;

namespace UberDashboard.App.Features.Weather;

/// <summary>
/// Fabryka instancji panelu pogody.
/// </summary>
public sealed class WeatherPanelFactory : IPanelFactory
{
    private readonly PanelStateScheduler _scheduler;
    private readonly IWeatherService _weatherService;
    private readonly IWeatherLocationSearchService _locationSearchService;
    private readonly IWeatherCacheStore _cacheStore;

    /// <summary>Tworzy fabrykę panelu pogody.</summary>
    public WeatherPanelFactory(
        PanelStateScheduler scheduler,
        IWeatherService weatherService,
        IWeatherLocationSearchService locationSearchService,
        IWeatherCacheStore cacheStore)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(weatherService);
        ArgumentNullException.ThrowIfNull(locationSearchService);
        ArgumentNullException.ThrowIfNull(cacheStore);

        _scheduler = scheduler;
        _weatherService = weatherService;
        _locationSearchService = locationSearchService;
        _cacheStore = cacheStore;
    }

    /// <inheritdoc />
    public ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var viewModel = new WeatherPanelViewModel(
            _weatherService,
            _locationSearchService,
            _cacheStore,
            context.Notifier);
        var instance = new WeatherPanelInstance(context, viewModel, _scheduler);

        return ValueTask.FromResult<IPanelInstance>(instance);
    }
}
