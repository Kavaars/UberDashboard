using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Synchronization;
using UberDashboard.Infrastructure.Integrations;

namespace UberDashboard.Infrastructure.DependencyInjection;

/// <summary>Explicit opt-in: the host must provide account consent/storage before activating synchronization.</summary>
public static class SynchronizationServiceCollectionExtensions
{
    public static IServiceCollection AddUberDashboardFileSynchronization(this IServiceCollection services)
    {
        services.AddSingleton<ISyncFileStore, SyncFileStore>();
        services.AddHttpClient<MicrosoftTodoFileProvider>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<GoogleDriveFileProvider>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ISyncFileProvider>(p => p.GetRequiredService<MicrosoftTodoFileProvider>());
        services.AddSingleton<ISyncFileProvider>(p => p.GetRequiredService<GoogleDriveFileProvider>());
        return services;
    }
    public static IServiceCollection AddUberDashboardSynchronization<TAccounts>(this IServiceCollection services)
        where TAccounts : class, IIntegrationAccountService
    {
        services.AddSingleton<IIntegrationAccountService, TAccounts>();
        services.AddHttpClient<IMicrosoftTodoProvider, MicrosoftTodoProvider>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<IGoogleCalendarProvider, GoogleCalendarProvider>(client => client.Timeout = TimeSpan.FromSeconds(30))
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<ISyncProvider>(p => p.GetRequiredService<IMicrosoftTodoProvider>());
        services.AddSingleton<ISyncProvider>(p => p.GetRequiredService<IGoogleCalendarProvider>());
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton<ISyncCoordinator>(p => p.GetRequiredService<SyncCoordinator>());
        services.AddSingleton<SyncScheduler>();
        return services;
    }
}
