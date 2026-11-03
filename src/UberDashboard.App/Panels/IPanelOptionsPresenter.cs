using CommunityToolkit.Mvvm.Input;

namespace UberDashboard.App.Panels;

/// <summary>Umożliwia powłoce pokazanie przycisku opcji w nagłówku panelu.</summary>
public interface IPanelOptionsPresenter : UberDashboard.Contracts.Panels.IPanelOptions
{
    /// <summary>Panel udostępnia opcje konfiguracyjne.</summary>
    new bool HasOptions { get; }

    /// <summary>Określa widoczność pływającego okna opcji.</summary>
    new bool IsOptionsOpen { get; }

    /// <summary>Przełącza widoczność okna opcji panelu.</summary>
    new IRelayCommand ToggleOptionsCommand { get; }
    bool UberDashboard.Contracts.Panels.IPanelOptions.HasOptions => HasOptions;
    bool UberDashboard.Contracts.Panels.IPanelOptions.IsOptionsOpen => IsOptionsOpen;
    System.Windows.Input.ICommand UberDashboard.Contracts.Panels.IPanelOptions.ToggleOptionsCommand => ToggleOptionsCommand;
}
