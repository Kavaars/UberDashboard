namespace UberDashboard.App.Settings;

/// <summary>Odczytuje i zapisuje trwałe ustawienia personalizacji aplikacji.</summary>
public interface IApplicationSettingsService
{
    /// <summary>Wczytuje ustawienia; brak zapisu zwraca wartości domyślne.</summary>
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Zapisuje ustawienia po normalizacji.</summary>
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
