using System.Windows;

namespace UberDashboard.App.Settings;

/// <summary>Okno ustawień aplikacji z zakładkami personalizacji i wyświetlania.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _initialized;

    /// <summary>Tworzy okno ustawień i podpina model widoku.</summary>
    public SettingsWindow(SettingsViewModel viewModel, UberDashboard.App.Plugins.PluginSettingsViewModel plugins)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _viewModel.RequestClose = Close;

        InitializeComponent();
        DataContext = _viewModel;
        PluginsTab.DataContext = plugins;
        AboutTab.DataContext = new AboutInfo();
        UberDashboard.App.Plugins.PluginFileDrop.Attach(this, plugins);
        Loaded += OnLoaded;
        Closing += async (_, e) =>
        {
            if (_viewModel.FlushAsync().IsCompleted) return;
            e.Cancel = true;
            await _viewModel.FlushAsync().ConfigureAwait(true);
            Close();
        };
    }

    internal Func<IReadOnlyList<Panels.PanelGroupSession>> TodoTargets { get; set; } = () => [];

    private void OpenMicrosoftTodo_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
            app.MicrosoftTodo?.Show(this, TodoTargets());
    }

    private void OpenSyncCenter_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app && app.Integrations is { } integrations)
            UberDashboard.App.Features.Integrations.SyncEditorWindow.OpenCenter(this, integrations);
    }

    public Task FlushAsync() => _viewModel.FlushAsync();

    private void OpenProjectWebsite_Click(object sender, RoutedEventArgs e) => OpenProjectLink(UberDashboard.Core.ProjectLinks.Website);
    private void OpenPrivacyPolicy_Click(object sender, RoutedEventArgs e) => OpenProjectLink(UberDashboard.Core.ProjectLinks.Privacy);

    private void OpenProjectLink(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _viewModel.NotifyProjectLinkFailure();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
