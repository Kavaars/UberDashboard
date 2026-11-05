using UberDashboard.Core.Localization;
namespace UberDashboard.App.Features.Weather;

/// <summary>
/// Mapowanie kodów WMO na opis i prosty glyph wyświetlany w panelu.
/// </summary>
internal static class WeatherConditionCatalog
{
    public static string Describe(int code) => code switch
    {
        0 => UiText.Get("Clear"),
        1 => UiText.Get("MostlySunny"),
        2 => UiText.Get("PartlyCloudy"),
        3 => UiText.Get("Overcast"),
        45 or 48 => UiText.Get("Fog"),
        51 or 53 or 55 => UiText.Get("Drizzle"),
        56 or 57 => UiText.Get("FreezingDrizzle"),
        61 or 63 or 65 => UiText.Get("Rain"),
        66 or 67 => UiText.Get("FreezingRain"),
        71 or 73 or 75 or 77 => UiText.Get("Snow"),
        80 or 81 or 82 => UiText.Get("RainShowers"),
        85 or 86 => UiText.Get("SnowShowers"),
        95 => UiText.Get("Thunderstorm"),
        96 or 99 => UiText.Get("ThunderstormWithHail"),
        _ => UiText.Get("UnknownConditions"),
    };

    public static string Glyph(int code, bool isDay) => code switch
    {
        0 => isDay ? "☀" : "☾",
        1 => isDay ? "☀" : "☾",
        2 => "⛅",
        3 => "☁",
        45 or 48 => "〰",
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "☂",
        71 or 73 or 75 or 77 or 85 or 86 => "❄",
        95 or 96 or 99 => "⚡",
        _ => "•",
    };
}
