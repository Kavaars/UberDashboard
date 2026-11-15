using UberDashboard.Core.Localization;
using System.Drawing;
using System.Windows.Forms;
using UberDashboard.App.Settings;

namespace UberDashboard.App.SystemIntegration;

/// <summary>WinForms NotifyIcon dla aplikacji WPF.</summary>
public sealed class TrayIconService : ITrayIconService
{
    private NotifyIcon? _notifyIcon;
    private ContextMenuStrip? _menu;
    private bool _required;
    private Icon? _icon;

    private bool ShouldShowIcon => _required || CurrentSettings.EnableTrayIcon;

    public ApplicationSettings CurrentSettings { get; private set; } = ApplicationSettings.Default.Normalize();

    public void Initialize(Action restoreMainWindow, Action resetMainWindow, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(restoreMainWindow);
        ArgumentNullException.ThrowIfNull(resetMainWindow);
        ArgumentNullException.ThrowIfNull(exitApplication);

        if (_notifyIcon is not null)
        {
            return;
        }

        var contextMenu = new ContextMenuStrip { ShowImageMargin = false, Padding = new Padding(4), Font = new Font("Segoe UI", 9.5f) };
        contextMenu.Opening += (_, _) =>
        {
            var surface = ThemeColor("Brush.Surface", SystemColors.Menu);
            var text = ThemeColor("Brush.Text", SystemColors.MenuText);
            var alternate = ThemeColor("Brush.Surface.Alt", SystemColors.MenuHighlight);
            var border = ThemeColor("Brush.Border", SystemColors.ControlDark);
            contextMenu.BackColor = surface;
            contextMenu.ForeColor = text;
            contextMenu.Renderer = new ToolStripProfessionalRenderer(new TrayColors(surface, alternate, border)) { RoundedEdges = false };
        };
        _menu = contextMenu;
        var grip = new Label { AutoSize = false, Height = 14, Cursor = Cursors.SizeAll, AccessibleName = UiText.Get("MoveMenu") };
        Point? dragStart = null;
        grip.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) dragStart = Cursor.Position; };
        grip.MouseMove += (_, e) =>
        {
            if (dragStart is not { } start || e.Button != MouseButtons.Left) return;
            var current = Cursor.Position;
            contextMenu.Location = new Point(contextMenu.Left + current.X - start.X, contextMenu.Top + current.Y - start.Y);
            dragStart = current;
        };
        grip.MouseUp += (_, _) => dragStart = null;
        grip.Paint += (_, e) =>
        {
            using var pill = new SolidBrush(ThemeColor("Brush.Border", SystemColors.ControlDark));
            e.Graphics.FillRectangle(pill, (grip.Width - 32) / 2, (grip.Height - 4) / 2, 32, 4);
        };
        contextMenu.Items.Add(new ToolStripControlHost(grip) { AutoSize = false, Height = 14, Width = 180, Margin = Padding.Empty, Padding = Padding.Empty });
        contextMenu.Items.Add(UiText.Get("ShowUberdashboard"), null, (_, _) => restoreMainWindow());
        contextMenu.Items.Add(UiText.Get("ResetWindowToPrimaryMonitor"), null, (_, _) => resetMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(UiText.Get("Quit"), null, (_, _) => exitApplication());

        using var iconStream = typeof(TrayIconService).Assembly.GetManifestResourceStream("UberDashboard.TrayIcon.ico");
        _icon = iconStream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(iconStream);
        _notifyIcon = new NotifyIcon
        {
            Text = "UberDashboard",
            Visible = ShouldShowIcon,
            ContextMenuStrip = contextMenu,
            Icon = _icon,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) restoreMainWindow();
        };
    }

    public void ApplySettings(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        CurrentSettings = settings.Normalize();
        UpdateVisibility();
    }

    public void SetRequired(bool required)
    {
        _required = required;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = ShouldShowIcon;
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
        _menu?.Dispose();
        _menu = null;
        _icon?.Dispose();
        _icon = null;
    }

    private static Color ThemeColor(string key, Color fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush
            ? Color.FromArgb(brush.Color.R, brush.Color.G, brush.Color.B) : fallback;

    private sealed class TrayColors(Color surface, Color alternate, Color border) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => surface;
        public override Color MenuItemSelected => alternate;
        public override Color MenuItemBorder => border;
        public override Color MenuBorder => border;
        public override Color SeparatorDark => border;
        public override Color SeparatorLight => surface;
    }

}
