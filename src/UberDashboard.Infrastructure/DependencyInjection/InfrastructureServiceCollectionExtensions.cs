using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Platform;
using UberDashboard.Application.Weather;
using UberDashboard.Infrastructure.Backup;
using UberDashboard.Infrastructure.Database;
using UberDashboard.Infrastructure.Database.Stores;
using UberDashboard.Infrastructure.Settings;
using UberDashboard.Infrastructure.Time;
using UberDashboard.Infrastructure.Weather;
using UberDashboard.Infrastructure.Work;

namespace UberDashboard.Infrastructure.DependencyInjection;

/// <summary>Rejestracja usług infrastruktury w kontenerze DI.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Dodaje ścieżki profilu, kolejkę pracy, bazę SQLite i magazyny danych.</summary>
    public static IServiceCollection AddUberDashboardInfrastructure(this IServiceCollection services, IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);
        services.AddSingleton<IClock>(SystemClock.Instance);

        services.AddSingleton(provider =>
            new SqliteDatabase(paths.DatabaseFile, provider.GetRequiredService<IClock>(), paths.BackupsDirectory));

        services.AddSingleton<IWorkQueue>(_ => new DatabaseWorkQueue());

        services.AddSingleton<IDashboardStore, DashboardStore>();
        services.AddSingleton<ILayoutStore, LayoutStore>();
        services.AddSingleton<IPanelInstanceStore, PanelInstanceStore>();
        services.AddSingleton<IPanelTabGroupStore, PanelTabGroupStore>();
        services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
        services.AddSingleton<UberDashboard.Application.Synchronization.ISyncStore, SyncStore>();
        services.AddSingleton<UberDashboard.Application.Synchronization.ICalendarViewCache, CalendarViewCache>();
        services.AddSingleton<IWindowModeSettingsService, WindowModeSettingsService>();
        services.AddSingleton<IWeatherCacheStore, WeatherCacheStore>();

        services.AddHttpClient<IWeatherService, OpenMeteoWeatherService>(client =>
        {
            client.BaseAddress = new Uri("https://api.open-meteo.com");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddHttpClient<IWeatherLocationSearchService, OpenMeteoLocationSearchService>(client =>
        {
            client.BaseAddress = new Uri("https://geocoding-api.open-meteo.com");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddSingleton<SqliteBackupService>();

        return services;
    }
}
