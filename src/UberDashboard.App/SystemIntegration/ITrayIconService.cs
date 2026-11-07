using UberDashboard.App.Settings;

namespace UberDashboard.App.SystemIntegration;

/// <summary>Obsługuje ikonę aplikacji w zasobniku systemowym.</summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>Aktualnie stosowane ustawienia zachowania aplikacji.</summary>
    ApplicationSettings CurrentSettings { get; }

    /// <summary>Inicjalizuje menu i akcje ikony zasobnika.</summary>
    void Initialize(Action restoreMainWindow, Action resetMainWindow, Action exitApplication);

    /// <summary>Stosuje ustawienia widoczności i zachowania zasobnika.</summary>
    void ApplySettings(ApplicationSettings settings);

    /// <summary>Wymusza widoczność ikony, gdy pasek jest zadokowany.</summary>
    void SetRequired(bool required);
}
