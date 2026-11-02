using System.Text.Json.Nodes;

namespace UberDashboard.Core.Layout.Serialization;

/// <summary>
/// Migracja dokumentu układu z jednej wersji schematu do następnej.
/// Migracja działa na kopii w pamięci i jest walidowana po zastosowaniu.
/// </summary>
public interface ILayoutSchemaMigration
{
    /// <summary>Wersja dokumentu wejściowego.</summary>
    int FromVersion { get; }

    /// <summary>Wersja dokumentu wyjściowego; zawsze <see cref="FromVersion"/> + 1.</summary>
    int ToVersion { get; }

    /// <summary>Zwraca nowy dokument w wersji <see cref="ToVersion"/>.</summary>
    JsonObject Apply(JsonObject document);
}

/// <summary>Rejestr migracji schematu układu, walidujący łańcuch wersji.</summary>
public sealed class LayoutSchemaMigrations
{
    private readonly Dictionary<int, ILayoutSchemaMigration> _bySourceVersion = [];

    /// <summary>Rejestr bez migracji — obsługiwana jest wyłącznie bieżąca wersja.</summary>
    public static LayoutSchemaMigrations Empty { get; } = new([]);

    /// <summary>Tworzy rejestr i sprawdza ciągłość łańcucha migracji.</summary>
    public LayoutSchemaMigrations(IEnumerable<ILayoutSchemaMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        foreach (var migration in migrations)
        {
            if (migration.ToVersion != migration.FromVersion + 1)
            {
                throw new ArgumentException(
                    $"Migracja {migration.FromVersion}→{migration.ToVersion} musi zwiększać wersję dokładnie o jeden.",
                    nameof(migrations));
            }

            if (!_bySourceVersion.TryAdd(migration.FromVersion, migration))
            {
                throw new ArgumentException(
                    $"Zduplikowana migracja dla wersji {migration.FromVersion}.",
                    nameof(migrations));
            }
        }
    }

    /// <summary>Liczba zarejestrowanych migracji.</summary>
    public int Count => _bySourceVersion.Count;

    /// <summary>Zwraca migrację dla wskazanej wersji źródłowej.</summary>
    public bool TryGetMigration(int fromVersion, out ILayoutSchemaMigration migration) =>
        _bySourceVersion.TryGetValue(fromVersion, out migration!);
}
