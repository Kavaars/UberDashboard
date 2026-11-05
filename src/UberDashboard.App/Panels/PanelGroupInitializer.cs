using UberDashboard.Core.Localization;
using UberDashboard.Application.Abstractions;
using UberDashboard.App.Features.Notes;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;

namespace UberDashboard.App.Panels;

/// <summary>Przygotowuje grupę przed aktywacją panelu; nie zastępuje danych po błędzie.</summary>
public sealed class PanelGroupInitializer(IPanelTabGroupStore groups, IPanelInstanceStore instances, IClock clock)
{
    public async Task<PanelTabGroup> EnsureAsync(PanelInstanceRecord root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var existing = await groups.GetAsync(root.InstanceId, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return existing.Group;
        PanelTabGroup group;
        IReadOnlyList<PanelInstanceRecord> records;
        if (root.PanelTypeId == NotesPanelDescriptor.TypeId && !string.IsNullOrWhiteSpace(root.StateJson))
        {
            var migration = NotesTabMigration.Prepare(root.InstanceId, root.StateJson);
            group = migration.Group;
            records = migration.Documents.Select(document => root with
            {
                InstanceId = document.InstanceId,
                StateJson = document.StateJson,
                IsHidden = document.InstanceId != root.InstanceId || root.IsHidden,
                CreatedAtUtc = document.InstanceId == root.InstanceId ? root.CreatedAtUtc : clock.UtcNow,
                UpdatedAtUtc = clock.UtcNow,
            }).ToArray();
        }
        else
        {
            group = PanelTabGroup.Create(root.InstanceId, root.DisplayName);
            records = [root];
        }
        await groups.CreateAsync(group, records, root.StateJson, cancellationToken).ConfigureAwait(false);
        return group;
    }

    public async Task<IReadOnlyList<PanelInstanceRecord>> ReadMembersAsync(PanelTabGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        var records = new List<PanelInstanceRecord>();
        foreach (var tab in group.Tabs)
        {
            var record = await instances.GetAsync(tab.InstanceId, cancellationToken).ConfigureAwait(false);
            if (record is null) throw new InvalidOperationException(UiText.Get("TabStateIsMissingTheGroupWas"));
            records.Add(record);
        }
        return records;
    }
}
