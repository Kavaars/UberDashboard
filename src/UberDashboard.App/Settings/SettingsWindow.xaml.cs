using System.Windows;

namespace UberDashboard.App.Settings;

/// <summary>Okno ustawień aplikacji z zakładkami personalizacji i wyświetlania.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private bool _initialized;

    /// <summary>Tworzy okno ustawień i podpina model widoku.</summary>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _viewModel.RequestClose = Close;

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
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
