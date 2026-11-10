using UberDashboard.Core.Localization;

namespace UberDashboard.App.Features.Weather;

/// <summary>One independently positioned weather reading.</summary>
public sealed class WeatherModule
{
    private readonly WeatherPanelViewModel _owner;
    public string Id { get; }
    internal WeatherModule(string id, WeatherPanelViewModel owner) { Id = id; _owner = owner; }
    public WeatherPanelViewModel Owner => _owner;
    public string Label => UiText.Get(Id switch { "location" => "Weather", "icon" => "ModuleWeatherIcon", "temperature" => "Temperature", "condition" => "Status", "range" => "HighLow", "sunrise" => "WeatherSunTimes", "sunset" => "Sunset", "wind" => "ModuleWeatherWind", "updated" => "ModuleWeatherUpdated", _ => "HourlyForecast" });
}
