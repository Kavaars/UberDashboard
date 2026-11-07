using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Platform;
using UberDashboard.App.Shell;

namespace UberDashboard.App.Tests.TestSupport;

internal sealed class FakeAppBarNativeApi : IAppBarNativeApi
{
    public List<string> Calls { get; } = [];

    public bool RegisterResult { get; set; } = true;

    public Func<AppBarEdge, PixelRect, PixelRect> QueryTransform { get; set; } = static (_, rect) => rect;

    public Func<AppBarEdge, PixelRect, PixelRect> SetTransform { get; set; } = static (_, rect) => rect;

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
        return QueryTransform(edge, requestedRect);
    }

    public PixelRect SetPosition(IntPtr windowHandle, AppBarEdge edge, PixelRect requestedRect)
    {
        Calls.Add("set");
        return SetTransform(edge, requestedRect);
    }

    public AppBarState GetState(IntPtr windowHandle) => AppBarState.None;

    public void Activate(IntPtr windowHandle, bool active = true)
    {
    }

    public void NotifyWindowPositionChanged(IntPtr windowHandle)
    {
    }

    public PixelRect GetTaskbarPosition() => new(0, 1040, 1920, 1080);
}

internal sealed class FakeMonitorProvider : IMonitorProvider
{
    public required IReadOnlyList<DisplayMonitor> Monitors { get; init; }

    public DisplayMonitor? WindowMonitor { get; init; }

    public IReadOnlyList<DisplayMonitor> Enumerate() => Monitors;

    public DisplayMonitor? GetByWindowHandle(IntPtr windowHandle) => WindowMonitor;

    public DisplayMonitor? GetByPoint(int x, int y) => Monitors.FirstOrDefault();
}

internal sealed class FakeWindowModeSettingsService : IWindowModeSettingsService
{
    public WindowModeSettings Settings { get; set; } = WindowModeSettings.Default;

    public Task<WindowModeSettings> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Settings);

    public Task SetAsync(WindowModeSettings settings, CancellationToken cancellationToken = default)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-11-02T08:00:00+00:00");
}

internal sealed class FakeAppSettingsStore : IAppSettingsStore
{
    private readonly Dictionary<string, string> _items = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        _items.TryGetValue(key, out var value);
        return Task.FromResult<string?>(value);
    }

    public Task SetAsync(string key, string valueJson, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        _items[key] = valueJson;
        return Task.CompletedTask;
    }
}

internal sealed class FakeDesktopWidgetWindowHost : IDesktopWidgetWindowHost
{
    public bool TryAttachResult { get; set; }

    public List<string> Calls { get; } = [];

    public Task<bool> TryAttachAsync(IntPtr windowHandle, double opacity, CancellationToken cancellationToken)
    {
        Calls.Add($"attach:{opacity:0.##}");
        return Task.FromResult(TryAttachResult);
    }

    public Task DetachAsync(IntPtr windowHandle, CancellationToken cancellationToken)
    {
        Calls.Add("detach");
        return Task.CompletedTask;
    }
}

internal sealed class FakeDockedBarWindowHost : IDockedBarWindowHost
{
    public bool ApplyResult { get; set; } = true;
    public PixelRect? AppliedBounds { get; private set; }
    public string? WaitingMonitorId { get; private set; }
    public void Configure(IntPtr handle, string monitorId, AppBarEdge edge, int thickness) => WaitingMonitorId = monitorId;
    public bool WaitForMonitor() => true;
    public bool Apply(IntPtr handle, PixelRect bounds) { AppliedBounds = bounds; return ApplyResult; }
    public void Restore() { }
}
