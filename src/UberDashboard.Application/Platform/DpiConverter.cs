namespace UberDashboard.Application.Platform;

/// <summary>
/// Konwersja między DIP (jednostkami WPF) a pikselami fizycznymi właściwymi dla
/// kontekstu DPI. WPF trzyma rozmiary w DIP, a granica Win32/AppBar pracuje na
/// pikselach, dlatego zaokrąglanie jest scentralizowane w tym miejscu.
/// </summary>
public static class DpiConverter
{
    /// <summary>Bazowe DPI odpowiadające skali 100%.</summary>
    public const double DefaultDpi = 96d;

    /// <summary>Przelicza piksele na DIP dla podanego DPI.</summary>
    public static double ToDip(int pixels, uint dpi) =>
        pixels * DefaultDpi / EffectiveDpi(dpi);

    /// <summary>
    /// Przelicza DIP na piksele, zaokrąglając połówki od zera. Wynik nadaje się
    /// bezpośrednio do współrzędnych okna Win32.
    /// </summary>
    public static int ToPixels(double dip, uint dpi) =>
        (int)Math.Round(dip * EffectiveDpi(dpi) / DefaultDpi, MidpointRounding.AwayFromZero);

    private static double EffectiveDpi(uint dpi) => dpi == 0 ? DefaultDpi : dpi;
}
