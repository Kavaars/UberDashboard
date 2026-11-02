namespace UberDashboard.App.SystemIntegration;

/// <summary>Rejestruje autostart aplikacji dla bieżącego użytkownika Windows.</summary>
public interface IStartupRegistrationService
{
    /// <summary>Sprawdza, czy autostart wskazuje bieżący plik wykonywalny.</summary>
    bool IsEnabled(string executablePath);

    /// <summary>Włącza lub wyłącza autostart aplikacji.</summary>
    void Apply(bool enabled, string executablePath);
}
