using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.App.Shell;
using UberDashboard.Application.Platform;

internal static class DockedBarChecks
{
    public static void Run(IServiceProvider provider)
    {
        var host = provider.GetRequiredService<IDockedBarWindowHost>();
        var window = new Window { ShowActivated = false, Left = -20000, Top = -20000 };
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        try
        {
            if (!host.Apply(handle, new PixelRect(-20000, -20000, -19680, -19880)))
                throw new InvalidOperationException("Could not apply docked window bounds.");
            if (window.ShowInTaskbar)
                throw new InvalidOperationException("Docked window still shows in the taskbar.");
            host.Restore();
            if (!window.ShowInTaskbar)
                throw new InvalidOperationException("Normal window did not return to the taskbar.");
        }
        finally
        {
            host.Restore();
            window.Close();
        }
        Console.WriteLine("DOCKED BAR TASKBAR PASS");
    }
}
