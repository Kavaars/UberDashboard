namespace UberDashboard.Application.Platform;

/// <summary>Położenie zwykłego okna w fizycznych pikselach, niezależne od geometrii paska.</summary>
public sealed record WindowPlacement(string MonitorId, PixelRect Bounds, bool Maximized)
{
    /// <summary>Utrzymuje okno w obszarze roboczym dostępnego monitora.</summary>
    public PixelRect FitTo(DisplayMonitor monitor)
    {
        var area = monitor.WorkArea.IsValid ? monitor.WorkArea : monitor.Bounds;
        var width = Math.Min(Math.Max(1, Bounds.Width), area.Width);
        var height = Math.Min(Math.Max(1, Bounds.Height), area.Height);
        var left = Math.Clamp(Bounds.Left, area.Left, area.Right - width);
        var top = Math.Clamp(Bounds.Top, area.Top, area.Bottom - height);
        return new PixelRect(left, top, left + width, top + height);
    }
}
