namespace UberDashboard.Application.Platform;

/// <summary>Koordynuje przejścia między trybami głównego okna.</summary>
public interface IWindowModeCoordinator
{
    /// <summary>Aktualnie aktywny tryb.</summary>
    WindowMode CurrentMode { get; }

    /// <summary><see langword="true"/>, gdy koordynator zna uchwyt okna.</summary>
    bool IsAttached { get; }

    /// <summary>Podpina uchwyt głównego okna do późniejszych operacji trybów.</summary>
    void AttachWindow(IntPtr windowHandle);

    /// <summary>Wczytuje ustawienia i stosuje tryb startowy.</summary>
    Task<WindowModeTransitionResult> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Przełącza aplikację do wskazanego trybu.</summary>
    Task<WindowModeTransitionResult> SwitchAsync(WindowMode mode, CancellationToken cancellationToken = default);
}

/// <summary>Wynik przejścia między trybami okna.</summary>
public sealed record WindowModeTransitionResult(bool IsSuccess, WindowMode ActiveMode, string? Message)
{
    /// <summary>Tworzy wynik udanego przełączenia.</summary>
    public static WindowModeTransitionResult Success(WindowMode mode) => new(true, mode, null);

    /// <summary>Tworzy wynik nieudanego przełączenia z podanym trybem aktywnym po fallbacku.</summary>
    public static WindowModeTransitionResult Fail(WindowMode fallbackMode, string message) => new(false, fallbackMode, message);
}
