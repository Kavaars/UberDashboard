using CommunityToolkit.Mvvm.Input;

namespace UberDashboard.App.Panels;

/// <summary>Umożliwia powłoce pokazanie przycisku opcji w nagłówku panelu.</summary>
public interface IPanelOptionsPresenter
{
    /// <summary>Panel udostępnia opcje konfiguracyjne.</summary>
    bool HasOptions { get; }

    /// <summary>Określa widoczność pływającego okna opcji.</summary>
    bool IsOptionsOpen { get; }

    /// <summary>Przełącza widoczność okna opcji panelu.</summary>
    IRelayCommand ToggleOptionsCommand { get; }
}
