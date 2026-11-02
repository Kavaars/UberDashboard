namespace UberDashboard.Contracts.Host;

/// <summary>
/// Namespacowany magazyn plików instancji panelu. Ścieżki są ograniczone do
/// katalogu instancji; interfejs nie pozwala usuwać dowolnych ścieżek.
/// </summary>
public interface IPanelStorage
{
    /// <summary>Odczytuje plik instancji lub <see langword="null"/>, gdy nie istnieje.</summary>
    ValueTask<byte[]?> ReadAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>Zapisuje plik instancji, zastępując poprzednią zawartość.</summary>
    ValueTask WriteAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    /// <summary>Usuwa plik instancji, jeśli istnieje.</summary>
    ValueTask DeleteAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>Zwraca nazwy plików znajdujących się w katalogu instancji.</summary>
    IReadOnlyList<string> ListFiles();
}
