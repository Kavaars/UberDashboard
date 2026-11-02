using Serilog;
using Serilog.Events;
using UberDashboard.Application.Abstractions;

namespace UberDashboard.Infrastructure.Logging;

/// <summary>
/// Konfiguracja logowania Serilog. Wpisy mają timestamp ISO 8601, poziom i moduł
/// (<c>SourceContext</c>), a pliki trafiają do katalogu <c>LOGS</c> jako
/// <c>app-YYYYMMDD.txt</c> z rotacją dzienną i ograniczoną retencją.
/// </summary>
public static class AppLogging
{
    /// <summary>Szablon wiersza logu: znacznik czasu, poziom, moduł i komunikat.</summary>
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    /// <summary>Liczba dni, przez które zachowywane są pliki logów.</summary>
    public const int RetainedFileCountLimit = 14;

    /// <summary>Tworzy konfigurację logera dla wskazanych ścieżek profilu.</summary>
    public static LoggerConfiguration CreateConfiguration(IAppPaths paths, bool includeConsole = true)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "app-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCountLimit,
                outputTemplate: OutputTemplate,
                shared: false);

        if (includeConsole)
        {
            configuration = configuration.WriteTo.Console(outputTemplate: OutputTemplate);
        }

        return configuration;
    }
}
