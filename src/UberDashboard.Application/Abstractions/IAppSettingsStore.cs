namespace UberDashboard.Application.Abstractions;

/// <summary>
/// Magazyn ustawień aplikacji. Kluczem jest krótki identyfikator, a wartością
/// dokument JSON, co pozwala rozszerzać model bez zmiany schematu tabeli.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>Zwraca wartość JSON ustawienia albo <see langword="null"/>.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Zapisuje wartość JSON ustawienia.</summary>
    Task SetAsync(string key, string valueJson, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
