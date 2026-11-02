using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Abstractions;

/// <summary>
/// Rekord instancji panelu. Stan właściwy panelu jest kluczowany po
/// <see cref="InstanceId"/>, dzięki czemu migracje i usuwanie działają per instancja.
/// </summary>
public sealed record PanelInstanceRecord(
    PanelInstanceId InstanceId,
    Guid DashboardId,
    string PluginId,
    string PanelTypeId,
    string DisplayName,
    bool IsHidden,
    string? ConfigJson,
    string? StateJson,
    string? RestoreHintJson,
    int DataSchemaVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Magazyn instancji paneli.</summary>
public interface IPanelInstanceStore
{
    /// <summary>Listuje wszystkie instancje pulpitu, także ukryte.</summary>
    Task<IReadOnlyList<PanelInstanceRecord>> ListForDashboardAsync(Guid dashboardId, CancellationToken cancellationToken = default);

    /// <summary>Zwraca instancję albo <see langword="null"/>.</summary>
    Task<PanelInstanceRecord?> GetAsync(PanelInstanceId instanceId, CancellationToken cancellationToken = default);

    /// <summary>Wstawia lub aktualizuje instancję.</summary>
    Task UpsertAsync(PanelInstanceRecord record, CancellationToken cancellationToken = default);

    /// <summary>Zmienia stan ukrycia wraz z opcjonalną wskazówką przywrócenia.</summary>
    Task SetHiddenAsync(
        PanelInstanceId instanceId,
        bool isHidden,
        string? restoreHintJson,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Trwale usuwa instancję.</summary>
    Task DeleteAsync(PanelInstanceId instanceId, CancellationToken cancellationToken = default);
}
