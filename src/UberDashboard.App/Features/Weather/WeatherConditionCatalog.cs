namespace UberDashboard.App.Features.Weather;

/// <summary>
/// Mapowanie kodów WMO na opis i prosty glyph wyświetlany w panelu.
/// </summary>
internal static class WeatherConditionCatalog
{
    public static string Describe(int code) => code switch
    {
        0 => "Bezchmurnie",
        1 => "Głównie słonecznie",
        2 => "Częściowe zachmurzenie",
        3 => "Pochmurno",
        45 or 48 => "Mgła",
        51 or 53 or 55 => "Mżawka",
        56 or 57 => "Marznąca mżawka",
        61 or 63 or 65 => "Deszcz",
        66 or 67 => "Marznący deszcz",
        71 or 73 or 75 or 77 => "Śnieg",
        80 or 81 or 82 => "Przelotny deszcz",
        85 or 86 => "Przelotny śnieg",
        95 => "Burza",
        96 or 99 => "Burza z gradem",
        _ => "Warunki nieznane",
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
