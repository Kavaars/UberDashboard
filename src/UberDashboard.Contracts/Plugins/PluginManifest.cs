namespace UberDashboard.Contracts.Plugins;

/// <summary>Zależność zewnętrzna deklarowana przez wtyczkę.</summary>
public sealed record PluginDependency
{
    /// <summary>Identyfikator pakietu lub komponentu.</summary>
    public required string Id { get; init; }

    /// <summary>Minimalna wymagana wersja.</summary>
    public string? MinVersion { get; init; }
}

/// <summary>Deklaracja pojedynczego panelu udostępnianego przez wtyczkę.</summary>
public sealed record PluginPanelDeclaration
{
    /// <summary>Unikalny w obrębie wtyczki identyfikator typu.</summary>
    public required string TypeId { get; init; }

    /// <summary>Nazwa prezentowana użytkownikowi.</summary>
    public required string Name { get; init; }
}

/// <summary>
/// Manifest wtyczki. Nazwa wydawcy w manifeście nie dowodzi tożsamości;
/// weryfikacja pochodzenia wymaga podpisu pakietu.
/// </summary>
public sealed record PluginManifest
{
    /// <summary>Identyfikator reverse-DNS; unikalny i niezmienny.</summary>
    public required string Id { get; init; }

    /// <summary>Nazwa wtyczki.</summary>
    public required string Name { get; init; }

    /// <summary>Deklarowany wydawca (informacja, nie dowód tożsamości).</summary>
    public string? Publisher { get; init; }

    /// <summary>Wersja wtyczki.</summary>
    public required string Version { get; init; }

    /// <summary>Wersja kontraktu API, z którą wtyczka została zbudowana.</summary>
    public required string ApiVersion { get; init; }

    /// <summary>Minimalna wersja hosta wymagana przez wtyczkę.</summary>
    public required string MinHostVersion { get; init; }

    /// <summary>Framework docelowy wtyczki, np. <c>net10.0-windows</c>.</summary>
    public required string TargetFramework { get; init; }

    /// <summary>Nazwa pliku assembly wejściowego.</summary>
    public required string EntryAssembly { get; init; }

    /// <summary>Pełna nazwa typu implementującego <c>IPluginModule</c>.</summary>
    public required string EntryType { get; init; }

    /// <summary>Wersja schematu danych stanu wtyczki.</summary>
    public int DataSchemaVersion { get; init; } = 1;

    /// <summary>Deklarowane zależności.</summary>
    public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];

    /// <summary>Deklarowane panele.</summary>
    public IReadOnlyList<PluginPanelDeclaration> Panels { get; init; } = [];

    /// <summary>Deklarowane funkcje; informacja i element zgody, nie mechanizm wymuszania uprawnień.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}
