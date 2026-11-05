using UberDashboard.Core.Localization;
namespace UberDashboard.Application.Platform;



/// <summary>

/// Jedyny właściciel rejestracji AppBar. Prowadzi cykl

/// <c>ABM_NEW</c> → <c>ABM_QUERYPOS</c> → dopasowanie grubości → <c>ABM_SETPOS</c> → <c>ABM_REMOVE</c>

/// i zawsze stosuje prostokąt zwrócony przez powłokę po <c>ABM_SETPOS</c>.

/// Operacje są idempotentne: powtórna rejestracja lub usunięcie nie zmienia stanu.

/// </summary>

public sealed class AppBarController

{

    private readonly IAppBarNativeApi _nativeApi;

    private bool _isRegistered;

    private IntPtr _windowHandle;

    private AppBarEdge _edge;

    private int _thickness;

    private PixelRect _monitorWorkArea;



    /// <summary>Tworzy kontroler nad warstwą wywołań natywnych.</summary>

    public AppBarController(IAppBarNativeApi nativeApi) =>

        _nativeApi = nativeApi ?? throw new ArgumentNullException(nameof(nativeApi));



    /// <summary>Pasek jest aktualnie zarejestrowany.</summary>

    public bool IsRegistered => _isRegistered;



    /// <summary>Uchwyt okna paska albo <see cref="IntPtr.Zero"/>, gdy pasek nie jest zarejestrowany.</summary>

    public IntPtr WindowHandle => _windowHandle;



    /// <summary>

    /// Rejestruje pasek na wybranej krawędzi i zwraca wiążący prostokąt. Powtórne wywołanie

    /// dla zarejestrowanego paska zwraca niepowodzenie bez zmiany stanu.

    /// </summary>

    public AppBarDockResult Register(IntPtr windowHandle, AppBarEdge edge, int thickness, PixelRect monitorWorkArea)

    {

        if (_isRegistered)

        {

            return AppBarDockResult.Fail(UiText.Get("TheDockedBarIsAlreadyRegistered"));

        }



        if (windowHandle == IntPtr.Zero)

        {

            return AppBarDockResult.Fail(UiText.Get("InvalidWindowHandle"));

        }



        if (thickness <= 0)

        {

            return AppBarDockResult.Fail(UiText.Get("DockThicknessMustBePositive"));

        }



        if (!monitorWorkArea.IsValid)

        {

            return AppBarDockResult.Fail(UiText.Get("TheDisplayWorkAreaIsInvalid"));

        }



        if (!_nativeApi.Register(windowHandle))

        {

            return AppBarDockResult.Fail(UiText.Get("WindowsRejectedTheDockedBarRegistration"));

        }



        _windowHandle = windowHandle;

        _edge = edge;

        _thickness = thickness;

        _monitorWorkArea = monitorWorkArea;



        var result = Apply();

        if (!result.IsSuccess)

        {

            _nativeApi.Unregister(windowHandle);

            _windowHandle = IntPtr.Zero;

            return result;

        }



        _isRegistered = true;

        return result;

    }



    /// <summary>

    /// Ponawia cykl pozycjonowania dla zarejestrowanego paska. Wywoływane przy

    /// <c>ABN_POSCHANGED</c> oraz po zmianie DPI lub konfiguracji monitorów.

    /// </summary>

    public AppBarDockResult Restart(PixelRect? monitorBounds = null)

    {

        if (!_isRegistered) return AppBarDockResult.Fail(UiText.Get("TheDockedBarIsNotRegistered"));

        var handle = _windowHandle;

        var edge = _edge;

        var thickness = _thickness;

        var area = monitorBounds ?? _monitorWorkArea;

        Remove();

        return Register(handle, edge, thickness, area);

    }



    public AppBarDockResult UpdateConfiguration(AppBarEdge edge, int thickness, PixelRect monitorBounds)

    {

        if (!_isRegistered || thickness <= 0 || !monitorBounds.IsValid) return AppBarDockResult.Fail(UiText.Get("InvalidDockConfiguration"));

        _edge = edge;

        _thickness = thickness;

        _monitorWorkArea = monitorBounds;

        return Apply();

    }



    public AppBarDockResult Reapply()

    {

        if (!_isRegistered)

        {

            return AppBarDockResult.Fail(UiText.Get("TheDockedBarIsNotRegistered"));

        }



        return Apply();

    }



    /// <summary>Aktualizuje zapamiętany obszar roboczy monitora i ponawia pozycjonowanie.</summary>

    public AppBarDockResult UpdateWorkArea(PixelRect monitorWorkArea)

    {

        if (!_isRegistered)

        {

            return AppBarDockResult.Fail(UiText.Get("TheDockedBarIsNotRegistered"));

        }



        if (!monitorWorkArea.IsValid)

        {

            return AppBarDockResult.Fail(UiText.Get("TheDisplayWorkAreaIsInvalid"));

        }



        _monitorWorkArea = monitorWorkArea;

        return Apply();

    }



    /// <summary>Wyrejestrowuje pasek. Wywołanie dla niezarejestrowanego paska nic nie robi.</summary>

    public void Remove()

    {

        if (!_isRegistered)

        {

            return;

        }



        _nativeApi.Unregister(_windowHandle);

        _isRegistered = false;

        _windowHandle = IntPtr.Zero;

    }



    /// <summary>Powiadamia powłokę o aktywacji okna paska.</summary>

    public void NotifyActivated(bool active = true)

    {

        if (_isRegistered)

        {

            _nativeApi.Activate(_windowHandle, active);

        }

    }



    /// <summary>Powiadamia powłokę o zmianie pozycji okna paska.</summary>

    public void NotifyWindowPositionChanged()

    {

        if (_isRegistered)

        {

            _nativeApi.NotifyWindowPositionChanged(_windowHandle);

        }

    }



    private AppBarDockResult Apply()

    {

        var requested = AppBarGeometry.RequestedRect(_monitorWorkArea, _edge, _thickness);

        var queried = _nativeApi.QueryPosition(_windowHandle, _edge, requested);

        var refitted = AppBarGeometry.RefitThickness(queried, _edge, _thickness);

        var final = _nativeApi.SetPosition(_windowHandle, _edge, refitted);



        return AppBarGeometry.IsUsable(final)

            ? AppBarDockResult.Ok(final)

            : AppBarDockResult.Fail(UiText.Get("WindowsDidNotReturnAValidDock"));

    }

}
