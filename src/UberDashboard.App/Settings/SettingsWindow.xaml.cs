using System.Windows;

namespace UberDashboard.App.Settings;

/// <summary>Okno ustawień aplikacji z zakładkami personalizacji i wyświetlania.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _initialized;
    private readonly UberDashboard.App.Features.Integrations.IntegrationDiagnostics? _diagnostics;

    /// <summary>Tworzy okno ustawień i podpina model widoku.</summary>
    public SettingsWindow(SettingsViewModel viewModel, UberDashboard.App.Plugins.PluginSettingsViewModel plugins, UberDashboard.Application.Synchronization.IIntegrationDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _diagnostics = diagnostics as UberDashboard.App.Features.Integrations.IntegrationDiagnostics;
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

    private async Task RefreshIntegrationLogAsync()
    {
        if (_diagnostics is null) return;
        IntegrationLogText.Text = await _diagnostics.ReadAsync();
        IntegrationLogStatus.Text = _diagnostics.WriteFailed ? UberDashboard.Core.Localization.UiText.Get("IntegrationLogWriteError") : "";
    }

    private async void IntegrationLogEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnostics is null) return;
        IntegrationLogEnabled.IsEnabled = false;
        var enabled = IntegrationLogEnabled.IsChecked == true;
        try { await Task.Run(() => _diagnostics.SetEnabledAsync(enabled)); IntegrationLogStatus.Text = ""; }
        catch (Exception) { IntegrationLogEnabled.IsChecked = !(IntegrationLogEnabled.IsChecked == true); IntegrationLogStatus.Text = UberDashboard.Core.Localization.UiText.Get("IntegrationLogError"); }
        finally { IntegrationLogEnabled.IsEnabled = true; }
    }

    private async void IntegrationLogRefresh_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshIntegrationLogAsync(); }
        catch (Exception) { IntegrationLogStatus.Text = UberDashboard.Core.Localization.UiText.Get("IntegrationLogError"); }
    }

    private async void IntegrationLogClear_Click(object sender, RoutedEventArgs e)
    {
        try { if (_diagnostics is not null) { await _diagnostics.ClearAsync(); await RefreshIntegrationLogAsync(); } }
        catch (Exception) { IntegrationLogStatus.Text = UberDashboard.Core.Localization.UiText.Get("IntegrationLogError"); }
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
        try
        {
            if (_diagnostics is not null) { IntegrationLogEnabled.IsChecked = await Task.Run(_diagnostics.LoadAsync); await RefreshIntegrationLogAsync(); }
            else IntegrationLogEnabled.IsEnabled = false;
        }
        catch (Exception) { IntegrationLogStatus.Text = UberDashboard.Core.Localization.UiText.Get("IntegrationLogError"); }
    }
}
