using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using UberDashboard.Application.Abstractions;
using UberDashboard.Infrastructure.DependencyInjection;
using UberDashboard.Infrastructure.Logging;
using UberDashboard.Infrastructure.Paths;
using UberDashboard.Platform.Windows.DependencyInjection;

namespace UberDashboard.App.Hosting;

/// <summary>
/// Buduje Generic Host jako composition root aplikacji: ścieżki profilu, logowanie
/// Serilog, infrastruktura SQLite, warstwa Windows (monitory/AppBar) oraz usługi
/// powłoki WPF. Host nie uruchamia długiego I/O w fazie startu — migracje bazy
/// wykonują się leniwie na kolejce pracy przy pierwszym dostępie.
/// </summary>
public static class AppHostBuilder
{
    /// <summary>Tworzy skonfigurowany, nierozwiązany jeszcze host.</summary>
    public static IHost Build(string? profileRoot = null)
    {
        var paths = new AppPaths(profileRoot);
        paths.EnsureCreated();

        Log.Logger = AppLogging.CreateConfiguration(paths, includeConsole: false).CreateLogger();

        var builder = new HostApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        builder.Services.AddSingleton<IAppPaths>(paths);
        builder.Services.AddUberDashboardInfrastructure(paths);
        builder.Services.AddUberDashboardWindows();
        builder.Services.AddUberDashboardApp();

        return builder.Build();
    }
}
