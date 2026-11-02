using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Platform;
using UberDashboard.Platform.Windows.Interop;
using UberDashboard.Platform.Windows.Monitors;

namespace UberDashboard.Platform.Windows.DependencyInjection;

/// <summary>Rejestracja adapterów powłoki Windows w kontenerze DI hosta.</summary>
public static class WindowsServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje wyliczanie monitorów, warstwę natywną AppBar i kontroler paska.
    /// Kontroler jest pojedynczą instancją, ponieważ jest jedynym właścicielem rejestracji.
    /// </summary>
    public static IServiceCollection AddUberDashboardWindows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMonitorProvider, Win32MonitorProvider>();
        services.AddSingleton<IAppBarNativeApi, Win32AppBarNativeApi>();
        services.AddSingleton<AppBarController>();

        return services;
    }
}
