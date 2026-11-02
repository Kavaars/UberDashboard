namespace UberDashboard.Application.Platform;

/// <summary>
/// Wyliczanie monitorów dla trybów okna, paska i pulpitu. Implementacja Win32
/// używa <c>EnumDisplayMonitors</c> i <c>GetMonitorInfo</c>; abstrakcja pozwala
/// hostowi oraz testom działać bez wywołań systemowych.
/// </summary>
public interface IMonitorProvider
{
    /// <summary>
    /// Zwraca wszystkie monitory znane powłoce, uporządkowane po identyfikatorze.
    /// Zwraca pustą listę, gdy powłoka nie udostępnia żadnego monitora.
    /// </summary>
    IReadOnlyList<DisplayMonitor> Enumerate();

    /// <summary>Zwraca monitor, na którym leży okno, albo <see langword="null"/>.</summary>
    DisplayMonitor? GetByWindowHandle(IntPtr windowHandle);

    /// <summary>Zwraca monitor zawierający punkt ekranu albo <see langword="null"/>.</summary>
    DisplayMonitor? GetByPoint(int x, int y);
}
