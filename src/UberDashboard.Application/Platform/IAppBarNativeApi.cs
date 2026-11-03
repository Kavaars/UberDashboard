namespace UberDashboard.Application.Platform;

/// <summary>
/// Cienka warstwa wywołań natywnych AppBar. Zawiera wyłącznie operacje, których
/// wymaga cykl rezerwacji obszaru roboczego; logika rejestracji żyje w
/// <see cref="AppBarController"/>, a implementacja Win32 — w warstwie platformy.
/// </summary>
public interface IAppBarNativeApi
{
    /// <summary>Rejestruje okno jako pasek (<c>ABM_NEW</c>). Zwraca <see langword="false"/>, gdy powłoka odmówi.</summary>
    bool Register(IntPtr windowHandle);

    /// <summary>Wyrejestrowuje pasek (<c>ABM_REMOVE</c>).</summary>
    bool Unregister(IntPtr windowHandle);

    /// <summary>
    /// Pyta powłokę o dozwolony prostokąt dla żądanej pozycji (<c>ABM_QUERYPOS</c>).
    /// System może zwrócić prostokąt przesunięty lub pomniejszony.
    /// </summary>
    PixelRect QueryPosition(IntPtr windowHandle, AppBarEdge edge, PixelRect requestedRect);

    /// <summary>
    /// Ustawia pozycję paska (<c>ABM_SETPOS</c>). Zwrócony prostokąt jest wiążący —
    /// to jego wartość należy zastosować do okna, a nie wartość żądaną.
    /// </summary>
    PixelRect SetPosition(IntPtr windowHandle, AppBarEdge edge, PixelRect requestedRect);

    /// <summary>Zwraca bieżący stan paska (<c>ABM_GETSTATE</c>).</summary>
    AppBarState GetState(IntPtr windowHandle);

    /// <summary>Powiadamia powłokę o aktywacji okna paska (<c>ABM_ACTIVATE</c>).</summary>
    void Activate(IntPtr windowHandle, bool active = true);

    /// <summary>Powiadamia powłokę o zmianie pozycji okna paska (<c>ABM_WINDOWPOSCHANGED</c>).</summary>
    void NotifyWindowPositionChanged(IntPtr windowHandle);

    /// <summary>Zwraca prostokąt paska zadań (<c>ABM_GETTASKBARPOS</c>).</summary>
    PixelRect GetTaskbarPosition();
}

/// <summary>
/// Wynik operacji dokowania paska. <see cref="Rect"/> jest prostokątem zwróconym
/// przez powłokę po <c>ABM_SETPOS</c> i to on obowiązuje przy ustawianiu okna.
/// </summary>
/// <param name="IsSuccess">Operacja zakończyła się poprawnym prostokątem.</param>
/// <param name="Rect">Wiążący prostokąt paska w pikselach albo <see cref="PixelRect.Empty"/> przy błędzie.</param>
/// <param name="Message">Czytelny opis niepowodzenia albo <see langword="null"/> przy sukcesie.</param>
public sealed record AppBarDockResult(bool IsSuccess, PixelRect Rect, string? Message)
{
    /// <summary>Wynik poprawny z prostokątem zwróconym przez powłokę.</summary>
    public static AppBarDockResult Ok(PixelRect rect) => new(true, rect, null);

    /// <summary>Wynik niepowodzenia z czytelnym powodem.</summary>
    public static AppBarDockResult Fail(string message) => new(false, PixelRect.Empty, message);
}
