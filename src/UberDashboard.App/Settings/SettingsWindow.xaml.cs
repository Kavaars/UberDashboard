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

    public Task FlushAsync() => _viewModel.FlushAsync();

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
