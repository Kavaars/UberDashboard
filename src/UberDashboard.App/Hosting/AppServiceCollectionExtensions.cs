using UberDashboard.Core.Localization;
using UberDashboard.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Panels;
using UberDashboard.Application.Weather;
using UberDashboard.App.Features.Notes;
using UberDashboard.App.Features.Calendar;
using UberDashboard.App.Features.Resources;
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

        services.AddSingleton<UberDashboard.App.Plugins.PluginManager>();
        services.AddSingleton<UberDashboard.App.Plugins.PluginSettingsViewModel>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IApplicationSettingsService, ApplicationSettingsService>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<IStartupRegistrationService, WindowsStartupRegistrationService>();
        services.AddSingleton<IColorPickerService, WindowsColorPickerService>();
        services.AddSingleton<IDockedBarWindowHost, DockedBarWindowHost>();
        services.AddSingleton<IDesktopWidgetWindowHost, DesktopWidgetWindowHost>();
        services.AddSingleton<IWindowModeCoordinator, MainWindowModeCoordinator>();
        services.AddSingleton<WindowPlacementTracker>();

        services.AddSingleton<IPanelRemovalConfirmation, PanelRemovalConfirmation>();
        services.AddSingleton<PanelStateScheduler>();
        services.AddSingleton<Features.Todo.MicrosoftTodoSession>();
        services.AddSingleton(provider => new Features.Todo.MicrosoftTodoSettings(provider.GetRequiredService<IAppSettingsStore>(),
            provider.GetRequiredService<IClock>(), provider.GetService<UberDashboard.Application.Synchronization.ISyncStore>(),
            provider.GetService<UberDashboard.Application.Synchronization.ISyncStore>() is null ? null : provider.GetRequiredService<UberDashboard.Application.Synchronization.ISyncCoordinator>()));
        UberDashboard.Infrastructure.DependencyInjection.SynchronizationServiceCollectionExtensions
            .AddUberDashboardSynchronization<Features.Integrations.IntegrationAccounts>(services);
        services.AddSingleton(provider => (Features.Integrations.IntegrationAccounts)provider
            .GetRequiredService<UberDashboard.Application.Synchronization.IIntegrationAccountService>());
        services.AddSingleton<Features.Integrations.IntegrationSyncService>();
        services.AddHostedService<IntegrationSyncWorker>();
        services.AddSingleton<Features.Todo.MicrosoftTodoRefresh>();
        services.AddSingleton<MicrosoftTodoWizard>();
        services.AddSingleton<PanelHostManager>();
        services.AddSingleton<PanelGroupInitializer>();
        services.AddSingleton<Func<PanelInstanceId, IPanelStateSink>>(
            _ => instanceId => throw new InvalidOperationException(
                UiText.Get("ThePanelStateSinkFactoryIsResolved")));

        services.AddSingleton<BuiltInPanelRegistration>(_ => new BuiltInPanelRegistration(Features.Space.SpacePanelDescriptor.Id, new Features.Space.SpacePanelDescriptor()));
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
            Features.Shortcuts.ShortcutsPanelDescriptor.Id, new Features.Shortcuts.ShortcutsPanelDescriptor(provider.GetRequiredService<PanelStateScheduler>())));
        services.AddSingleton<HardwareSampler>();
        services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
            CalendarPanelDescriptor.Id, new CalendarPanelDescriptor(provider.GetRequiredService<PanelStateScheduler>(),
                provider.GetService<UberDashboard.Application.Synchronization.ISyncStore>() is null ? null : provider.GetRequiredService<Features.Integrations.IntegrationSyncService>(),
                provider.GetService<UberDashboard.Application.Synchronization.ISyncStore>() is null ? null : provider.GetRequiredService<Features.Integrations.IntegrationAccounts>())));
        services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
            ResourcePanelDescriptor.Id, new ResourcePanelDescriptor(provider.GetRequiredService<HardwareSampler>())));

        services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
            "affirmations",
            new Features.Affirmations.AffirmationPanelDescriptor(new Features.Affirmations.AffirmationPanelFactory(provider.GetRequiredService<PanelStateScheduler>()))));

        services.AddSingleton<BuiltInPanelRegistration>(provider => new BuiltInPanelRegistration(
            NotesPanelDescriptor.TypeId,
            new NotesPanelDescriptor(new NotesPanelFactory(provider.GetRequiredService<PanelStateScheduler>()))));

        services.AddSingleton<BuiltInPanelRegistration>(provider =>
        {
            var scheduler = provider.GetRequiredService<PanelStateScheduler>();
            var factory = provider.GetService<IPanelInstanceStore>() is { } instances
                ? new TodoPanelFactory(scheduler, instances, provider.GetRequiredService<MicrosoftTodoSettings>(),
                    provider.GetRequiredService<Features.Integrations.IntegrationSyncService>(), provider.GetRequiredService<Features.Todo.MicrosoftTodoSession>())
                : new TodoPanelFactory(scheduler);
            return new BuiltInPanelRegistration(TodoPanelDescriptor.TypeId, new TodoPanelDescriptor(factory));
        });

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
                    UiText.Format("ThePanelTypeIsRegisteredMoreThan", registration.Descriptor.TypeId));
            }
        }

        return catalog;
    }
}
