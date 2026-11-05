using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.App.Shell;
using UberDashboard.App.Toasts;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Platform;
using UberDashboard.Platform.Windows.Interop;

internal static class WindowPlacementChecks
{
    public static async Task RunAsync(IServiceProvider provider)
    {
        var store = provider.GetRequiredService<IAppSettingsStore>();
        var clock = provider.GetRequiredService<IClock>();
        var monitors = provider.GetRequiredService<IMonitorProvider>();
        var toasts = provider.GetRequiredService<IToastService>();
        var modes = new Modes();
        var tracker = new WindowPlacementTracker(store, clock, monitors, modes, toasts);
        var monitor = monitors.Enumerate().First(m => m.IsUsable);
        var area = monitor.WorkArea;
        var expected = new PixelRect(area.Left + 30, area.Top + 30,
            area.Left + Math.Min(700, area.Width - 30), area.Top + Math.Min(500, area.Height - 30));
        var first = new Window { ShowActivated = false, Width = 640, Height = 480 };
        var second = new Window { ShowActivated = false };
        try
        {
            first.Show();
            var handle = new WindowInteropHelper(first).Handle;
            if (!ShellWindowInterop.ApplyBounds(handle, expected)) throw new InvalidOperationException("Cannot position test window.");
            tracker.Start(first);
            await tracker.FlushAsync();

            // A dock's geometry must never replace the ordinary window's placement.
            modes.CurrentMode = WindowMode.DockedBar;
            ShellWindowInterop.ApplyBounds(handle, new PixelRect(area.Left, area.Top, area.Right, area.Top + 200));
            tracker.Capture();
            await tracker.FlushAsync();

            second.Show();
            await new WindowPlacementTracker(store, clock, monitors, modes, toasts).RestoreAsync(second);
            var restored = ShellWindowInterop.ReadBounds(new WindowInteropHelper(second).Handle);
            if (restored != expected) throw new InvalidOperationException($"Placement roundtrip failed: {restored} != {expected}.");

            modes.CurrentMode = WindowMode.Normal;
            ShellWindowInterop.ApplyBounds(handle, expected);
            first.WindowState = WindowState.Maximized;
            tracker.Capture();
            await tracker.FlushAsync();
            await new WindowPlacementTracker(store, clock, monitors, modes, toasts).RestoreAsync(second);
            if (second.WindowState != WindowState.Maximized) throw new InvalidOperationException("Maximized state was not restored.");
        }
        finally
        {
            first.Close();
            second.Close();
        }
        Console.WriteLine("WINDOW PLACEMENT ROUNDTRIP / DOCK ISOLATION / MAXIMIZED PASS");
    }

    private sealed class Modes : IWindowModeCoordinator
    {
        public WindowMode CurrentMode { get; set; }
        public bool IsAttached => true;
        public void AttachWindow(IntPtr handle) { }
        public Task<WindowModeTransitionResult> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(WindowModeTransitionResult.Success(CurrentMode));
        public Task<WindowModeTransitionResult> SwitchAsync(WindowMode mode, CancellationToken cancellationToken = default) =>
            Task.FromResult(WindowModeTransitionResult.Success(CurrentMode = mode));
    }
}
