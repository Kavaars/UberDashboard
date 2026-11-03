using System.Windows.Input;

namespace UberDashboard.Contracts.Panels;

/// <summary>Opcjonalny kontrakt opcji, implementowany przez ViewModel (lub widok WPF) panelu.</summary>
public interface IPanelOptions
{
    bool HasOptions { get; }
    bool IsOptionsOpen { get; }
    ICommand ToggleOptionsCommand { get; }
}

/// <summary>Zmiany stanu zgłaszane hostowi; zdarzenie i capture wywoływane na wątku UI.</summary>
public interface IPanelStateNotifications
{
    event EventHandler? StateChanged;
    long ChangeVersion { get; }
    void OnStatePersisted();
}

/// <summary>Opcjonalny widok własny, np. UserControl WPF. Powłoka zachowuje oryginalny ViewModel.</summary>
public interface IPanelContent
{
    object Content { get; }
}

/// <summary>Opcjonalne powiadomienie o dostępnym rozmiarze treści panelu w DIP, na wątku UI.</summary>
public interface IResponsivePanel
{
    void OnPanelSizeChanged(double width, double height);
}
