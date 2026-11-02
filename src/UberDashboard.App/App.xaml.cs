using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using UberDashboard.App.Hosting;
using WpfMessageBox = System.Windows.MessageBox;

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
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        TryShutdownHost();
        Log.CloseAndFlush();
        base.OnExit(e);
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
