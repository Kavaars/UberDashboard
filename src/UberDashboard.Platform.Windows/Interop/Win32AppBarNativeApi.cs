using System.Runtime.InteropServices;
using UberDashboard.Application.Platform;

namespace UberDashboard.Platform.Windows.Interop;

/// <summary>
/// Implementacja <see cref="IAppBarNativeApi"/> oparta o <c>SHAppBarMessage</c>.
/// Warstwa tłumaczy wyłącznie typy zarządzane na natywne i z powrotem; logika
/// cyklu rejestracji żyje w <see cref="AppBarController"/>.
/// </summary>
internal sealed class Win32AppBarNativeApi : IAppBarNativeApi
{
    /// <inheritdoc />
    public bool Register(IntPtr windowHandle)
    {
        var data = CreateData(windowHandle);
        data.CallbackMessage = ShellWindowInterop.AppBarCallbackMessage;
        return NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data) != IntPtr.Zero;
    }

    /// <inheritdoc />
    public bool Unregister(IntPtr windowHandle)
    {
        var data = CreateData(windowHandle);
        return NativeMethods.SHAppBarMessage(NativeMethods.AbmRemove, ref data) != IntPtr.Zero;
    }

    /// <inheritdoc />
    public PixelRect QueryPosition(IntPtr windowHandle, AppBarEdge edge, PixelRect requestedRect)
    {
        var data = CreatePositionData(windowHandle, edge, requestedRect);
        NativeMethods.SHAppBarMessage(NativeMethods.AbmQueryPos, ref data);
        return ToManaged(data.Rect);
    }

    /// <inheritdoc />
    public PixelRect SetPosition(IntPtr windowHandle, AppBarEdge edge, PixelRect requestedRect)
    {
        var data = CreatePositionData(windowHandle, edge, requestedRect);
        NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);
        return ToManaged(data.Rect);
    }

    /// <inheritdoc />
    public AppBarState GetState(IntPtr windowHandle)
    {
        var data = CreateData(windowHandle);
        var state = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref data);
        return (AppBarState)(state.ToInt64() & (long)(AppBarState.AutoHide | AppBarState.AlwaysOnTop));
    }

    /// <inheritdoc />
    public void Activate(IntPtr windowHandle, bool active = true)
    {
        var data = CreateData(windowHandle);
        data.Param = active ? new IntPtr(1) : IntPtr.Zero;
        NativeMethods.SHAppBarMessage(NativeMethods.AbmActivate, ref data);
    }

    /// <inheritdoc />
    public void NotifyWindowPositionChanged(IntPtr windowHandle)
    {
        var data = CreateData(windowHandle);
        NativeMethods.SHAppBarMessage(NativeMethods.AbmWindowPosChanged, ref data);
    }

    /// <inheritdoc />
    public PixelRect GetTaskbarPosition()
    {
        var data = CreateData(IntPtr.Zero);
        NativeMethods.SHAppBarMessage(NativeMethods.AbmGetTaskbarPos, ref data);
        return ToManaged(data.Rect);
    }

    private static AppBarData CreateData(IntPtr windowHandle) => new()
    {
        Size = Marshal.SizeOf<AppBarData>(),
        WindowHandle = windowHandle,
    };

    private static AppBarData CreatePositionData(IntPtr windowHandle, AppBarEdge edge, PixelRect rect)
    {
        var data = CreateData(windowHandle);
        data.Edge = (uint)edge;
        data.Rect = ToNative(rect);
        return data;
    }

    private static NativeRect ToNative(PixelRect rect) => new()
    {
        Left = rect.Left,
        Top = rect.Top,
        Right = rect.Right,
        Bottom = rect.Bottom,
    };

    private static PixelRect ToManaged(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
