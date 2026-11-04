using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;

namespace UberDashboard.Application.Abstractions;

/// <summary>Odczytany skład grupy i zachowany przed migracją stan.</summary>
public sealed record StoredPanelTabGroup(PanelTabGroup Group, string? OriginalStateJson);

/// <summary>Transakcyjny magazyn grup paneli, niezależny od warstwy UI.</summary>
public interface IPanelTabGroupStore
{
    Task CreateAsync(PanelTabGroup group, IReadOnlyList<PanelInstanceRecord> records, string? originalStateJson, CancellationToken cancellationToken = default);
    Task AddAsync(PanelTabGroup expected, PanelTabGroup updated, PanelInstanceRecord added, CancellationToken cancellationToken = default);
    Task RemoveAsync(PanelTabGroup expected, PanelTabGroup updated, PanelInstanceId removed, CancellationToken cancellationToken = default);
    Task DeleteAsync(PanelTabGroup expected, CancellationToken cancellationToken = default);
    Task UpdateAsync(PanelTabGroup expected, PanelTabGroup updated, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<PanelInstanceId>> GetChildInstanceIdsAsync(Guid dashboardId, CancellationToken cancellationToken = default);
    Task<StoredPanelTabGroup?> GetAsync(PanelInstanceId groupId, CancellationToken cancellationToken = default);
}
