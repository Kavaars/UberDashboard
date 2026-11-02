using System.Drawing;
using System.Windows.Forms;
using UberDashboard.App.Settings;

namespace UberDashboard.App.SystemIntegration;

/// <summary>WinForms NotifyIcon dla aplikacji WPF.</summary>
public sealed class TrayIconService : ITrayIconService
{
    private NotifyIcon? _notifyIcon;

    public ApplicationSettings CurrentSettings { get; private set; } = ApplicationSettings.Default.Normalize();

    public void Initialize(Action restoreMainWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(restoreMainWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        if (_notifyIcon is not null)
        {
            return;
        }

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Pokaż UberDashboard", null, (_, _) => restoreMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Zakończ", null, (_, _) => exitApplication());

        _notifyIcon = new NotifyIcon
        {
            Text = "UberDashboard",
            Visible = CurrentSettings.EnableTrayIcon,
            ContextMenuStrip = contextMenu,
            Icon = ResolveIcon(),
        };

        _notifyIcon.DoubleClick += (_, _) => restoreMainWindow();
        _notifyIcon.Click += (_, _) => restoreMainWindow();
    }

    public void ApplySettings(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        CurrentSettings = settings.Normalize();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = CurrentSettings.EnableTrayIcon;
        }
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private static Icon ResolveIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return Icon.ExtractAssociatedIcon(processPath) ?? SystemIcons.Application;
        }

        return SystemIcons.Application;
    }
}
