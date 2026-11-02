using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Panels;
using UberDashboard.Application.Weather;
using UberDashboard.App.Features.Notes;
using UberDashboard.App.Features.Todo;
using UberDashboard.App.Features.Weather;
using UberDashboard.App.Panels;
using UberDashboard.App.Shell;
using UberDashboard.App.Settings;
using UberDashboard.App.SystemIntegration;
using UberDashboard.App.Theming;
using UberDashboard.App.Toasts;
using UberDashboard.Core.Layout;
using UberDashboard.Application.Platform;

namespace UberDashboard.App.Hosting;

/// <summary>
/// Rejestruje usługi warstwy aplikacji: motyw, toasty, host paneli z autozapisem
/// oraz wbudowane typy paneli (Notatnik, ToDo, Pogoda). Rejestr typów paneli jest budowany
/// z zebranych deskryptorów, dzięki czemu dodanie nowego wbudowanego panelu to
/// jedna nowa rejestracja, bez zmian w powłoce.
/// </summary>
public static class AppServiceCollectionExtensions
{
    /// <summary>Identyfikator wtyczki hosta dla wbudowanych paneli.</summary>
    public const string BuiltInPluginId = "pl.uberdashboard.builtin";

    /// <summary>Dodaje usługi powłoki WPF i wbudowane panele.</summary>
    public static IServiceCollection AddUberDashboardApp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ThemeService>();
        services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IStartupRegistrationService, WindowsStartupRegistrationService>();
        services.AddSingleton<IColorPickerService, WindowsColorPickerService>();
        services.AddSingleton<IDesktopWidgetWindowHost, DesktopWidgetWindowHost>();
        services.AddSingleton<IWindowModeCoordinator, MainWindowModeCoordinator>();

        services.AddSingleton<PanelStateScheduler>();
        services.AddSingleton<PanelHostManager>();
        services.AddSingleton<Func<PanelInstanceId, IPanelStateSink>>(
            _ => instanceId => throw new InvalidOperationException(
                "Fabryka ujścia stanu panelu jest rozwiązywana wyłącznie przez kontener dla konkretnej instancji."));

        RegisterBuiltInPanels(services);

        services.AddSingleton(CreateCatalog);
        services.AddSingleton<PanelCatalog.MinSizeSource>();
        services.AddSingleton<IPanelMinSizeSource>(provider => provider.GetRequiredService<PanelCatalog.MinSizeSource>());

        services.AddTransient<ShellViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<Func<SettingsWindow>>(provider => () => provider.GetRequiredService<SettingsWindow>());
        services.AddTransient<MainWindow>();

        return services;
    }

    private static void RegisterBuiltInPanels(IServiceCollection services)
    {
        services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
            NotesPanelDescriptor.TypeId,
            new NotesPanelDescriptor(new NotesPanelFactory(provider.GetRequiredService<PanelStateScheduler>()))));

        services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
            TodoPanelDescriptor.TypeId,
            new TodoPanelDescriptor(new TodoPanelFactory(provider.GetRequiredService<PanelStateScheduler>()))));

        services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
            WeatherPanelDescriptor.TypeId,
            new WeatherPanelDescriptor(new WeatherPanelFactory(
                provider.GetRequiredService<PanelStateScheduler>(),
                provider.GetRequiredService<IWeatherService>(),
                provider.GetRequiredService<IWeatherLocationSearchService>(),
                provider.GetRequiredService<IWeatherCacheStore>()))));
    }

    private static PanelCatalog CreateCatalog(IServiceProvider provider)
    {
        var catalog = new PanelCatalog();

        foreach (var registration in provider.GetServices<BuiltInPanelRegistration>())
        {
            if (!catalog.TryRegister(BuiltInPluginId, registration.Descriptor))
            {
                throw new InvalidOperationException(
                    $"Typ panelu '{registration.Descriptor.TypeId}' jest zarejestrowany więcej niż raz.");
            }
        }

        return catalog;
    }
}
