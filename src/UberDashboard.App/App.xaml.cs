using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using UberDashboard.App.Hosting;
using WpfMessageBox = UberDashboard.App.Controls.ThemedMessageBox;

namespace UberDashboard.App;

/// <summary>
/// Klasa główna aplikacji WPF. Tworzy Generic Host jako composition root, wstrzykuje
/// zależności do okna głównego i steruje integracją cyklu życia hosta z cyklem WPF.
/// Długie operacje (odczyt bazy, migracje) wykonują się poza wątkiem UI na kolejce
/// pracy, więc start okna nie blokuje interfejsu.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private Features.Todo.MicrosoftTodoRefresh? _todoRefresh;
    internal Settings.MicrosoftTodoWizard? MicrosoftTodo => _host?.Services.GetRequiredService<Settings.MicrosoftTodoWizard>();

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _host = AppHostBuilder.Build();
            _host.Start();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Nie udało się uruchomić hosta aplikacji.");
            Log.CloseAndFlush();
            WpfMessageBox.Show(
                $"Nie udało się uruchomić UberDashboard:\n{ex.Message}",
                "UberDashboard",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
        _todoRefresh = _host.Services.GetRequiredService<Features.Todo.MicrosoftTodoRefresh>();
        _todoRefresh.Start();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _todoRefresh?.Dispose();
        TryShutdownHost();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    /// <summary>Opróżnia autozapis i zwalnia host przed zniknięciem działającego Dispatchera.</summary>
    public async Task<bool> PrepareShutdownAsync()
    {
        if (_host is null) return true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _host.Services.GetRequiredService<UberDashboard.App.Plugins.PluginSettingsViewModel>().StopAsync().WaitAsync(timeout.Token).ConfigureAwait(true);
            if (MainWindow?.DataContext is UberDashboard.App.Shell.ShellViewModel shell)
            {
                await shell.FlushAppearancesAsync().WaitAsync(timeout.Token).ConfigureAwait(true);
                await shell.FlushTabGroupsAsync(timeout.Token).ConfigureAwait(true);
            }
            foreach (var settingsWindow in Windows.OfType<UberDashboard.App.Settings.SettingsWindow>().ToArray())
                await settingsWindow.FlushAsync().WaitAsync(timeout.Token).ConfigureAwait(true);
            await _host.Services.GetRequiredService<UberDashboard.App.Panels.PanelStateScheduler>().FlushAllAsync(timeout.Token).ConfigureAwait(true);
            await _host.StopAsync(timeout.Token).ConfigureAwait(true);
            if (_host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync().ConfigureAwait(true);
            else _host.Dispose();
            _host = null;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Nie zakończono zamykania: zapis danych nie został potwierdzony.");
            return false;
        }
    }

    private void TryShutdownHost()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            // Ograniczony czas na flush kolejki zapisów; brak blokowania procesu na zawsze.
            _host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Błąd podczas zatrzymywania hosta.");
        }
        finally
        {
            _host.Dispose();
            _host = null;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nieobsłużony wyjątek wątku UI.");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Nieobsłużony wyjątek domeny aplikacji.");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Nieobserwowany wyjątek zadania.");
        e.SetObserved();
    }
}
