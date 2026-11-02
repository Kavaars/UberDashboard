namespace UberDashboard.App.Settings;

/// <summary>Określa reakcję aplikacji na zamknięcie głównego okna.</summary>
public enum CloseBehavior
{
    /// <summary>Kończy proces aplikacji i zamyka host.</summary>
    ExitApplication = 0,

    /// <summary>Ukrywa okno i minimalizuje aplikację do zasobnika systemowego.</summary>
    MinimizeToTray = 1,
}
