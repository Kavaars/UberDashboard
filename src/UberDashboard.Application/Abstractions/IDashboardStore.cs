namespace UberDashboard.Application.Abstractions;

/// <summary>Skrócony opis pulpitu używany w selektorze pulpitów.</summary>
public sealed record DashboardSummary(
    Guid DashboardId,
    string Name,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsActive)
{
    /// <summary>Zwraca samą nazwę, aby selektory UI nie pokazywały technicznego dumpa rekordu.</summary>
    public override string ToString() => Name;
}

/// <summary>
/// Magazyn pulpitów. Implementacja wykonuje operacje bazy na kolejce pracy,
/// dlatego kontrakt jest asynchroniczny, mimo że SQLite nie jest w pełni asynchroniczny.
/// </summary>
public interface IDashboardStore
{
    /// <summary>Listuje pulpity w kolejności utworzenia.</summary>
    Task<IReadOnlyList<DashboardSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Zwraca pulpit albo <see langword="null"/>, gdy nie istnieje.</summary>
    Task<DashboardSummary?> GetAsync(Guid dashboardId, CancellationToken cancellationToken = default);

    /// <summary>Tworzy nowy pulpit o wskazanej nazwie.</summary>
    Task<DashboardSummary> CreateAsync(string name, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Zmienia nazwę pulpitu.</summary>
    Task<DashboardSummary> RenameAsync(Guid dashboardId, string name, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Usuwa pulpit wraz z jego układem i instancjami paneli.</summary>
    Task DeleteAsync(Guid dashboardId, CancellationToken cancellationToken = default);

    /// <summary>Zwraca identyfikator aktywnego pulpitu albo <see langword="null"/>.</summary>
    Task<Guid?> GetActiveDashboardIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Ustawia aktywny pulpit.</summary>
    Task SetActiveDashboardAsync(Guid dashboardId, CancellationToken cancellationToken = default);
}
