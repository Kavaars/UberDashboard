using UberDashboard.Application.Platform;

namespace UberDashboard.Application.Tests.TestSupport;

/// <summary>
/// Warstwa AppBar w pamięci. Rejestruje kolejność wywołań i pozwala sterować
/// odpowiedziami powłoki, aby sprawdzić zachowanie <see cref="AppBarController"/>
/// bez rzeczywistych wywołań systemowych.
/// </summary>
internal sealed class FakeAppBarNativeApi : IAppBarNativeApi
{
    public List<string> Calls { get; } = [];

    public bool RegisterResult { get; set; } = true;

    public AppBarState State { get; set; } = AppBarState.None;

    /// <summary>Przekształcenie odpowiedzi <c>ABM_QUERYPOS</c>; domyślnie bez zmian.</summary>
    public Func<AppBarEdge, PixelRect, PixelRect> QueryTransform { get; set; } = static (_, rect) => rect;

    /// <summary>Przekształcenie odpowiedzi <c>ABM_SETPOS</c>; domyślnie bez zmian.</summary>
    public Func<AppBarEdge, PixelRect, PixelRect> SetTransform { get; set; } = static (_, rect) => rect;

    public PixelRect LastQueried { get; private set; }

    public PixelRect LastSet { get; private set; }

    public bool Register(IntPtr windowHandle)
    {
        Calls.Add("register");
        return RegisterResult;
    }

    public bool Unregister(IntPtr windowHandle)
    {
        Calls.Add("unregister");
        return true;
    }

    public PixelRect QueryPosition(IntPtr windowHandle, AppBarEdge edge, PixelRect requestedRect)
    {
        Calls.Add("query");
        LastQueried = requestedRect;
        return QueryTransform(edge, requestedRect);
    }

    public PixelRect SetPosition(IntPtr windowHandle, AppBarEdge edge, PixelRect requestedRect)
    {
        Calls.Add("set");
        LastSet = requestedRect;
        return SetTransform(edge, requestedRect);
    }

    public AppBarState GetState(IntPtr windowHandle)
    {
        Calls.Add("state");
        return State;
    }

    public void Activate(IntPtr windowHandle) => Calls.Add("activate");

    public void NotifyWindowPositionChanged(IntPtr windowHandle) => Calls.Add("poschanged");

    public PixelRect GetTaskbarPosition() => new(0, 1040, 1920, 1080);
}
