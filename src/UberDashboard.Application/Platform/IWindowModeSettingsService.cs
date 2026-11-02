namespace UberDashboard.Application.Platform;

/// <summary>Odczyt i zapis ustawień trybów okna.</summary>
public interface IWindowModeSettingsService
{
    /// <summary>Zwraca zapisane ustawienia lub wartości domyślne.</summary>
    Task<WindowModeSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Zapisuje ustawienia trybów okna.</summary>
    Task SetAsync(WindowModeSettings settings, CancellationToken cancellationToken = default);
}
