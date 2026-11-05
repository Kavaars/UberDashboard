using UberDashboard.Core.Localization;
using System.Collections.Immutable;
using UberDashboard.Core.Layout;

namespace UberDashboard.Core.Panels;

/// <summary>Osobna instancja panelu i nazwa jej zakładki.</summary>
public sealed record PanelTab(PanelInstanceId InstanceId, string Name);

/// <summary>Niezmienny opis kolejności zakładek panelu i aktywnej instancji.</summary>
public sealed class PanelTabGroup
{
    private PanelTabGroup(PanelInstanceId groupId, ImmutableArray<PanelTab> tabs, PanelInstanceId activeId)
    {
        GroupId = groupId;
        Tabs = tabs;
        ActiveId = activeId;
    }

    public PanelInstanceId GroupId { get; }
    public ImmutableArray<PanelTab> Tabs { get; }
    public PanelInstanceId ActiveId { get; }

    public static PanelTabGroup Create(PanelInstanceId id, string name)
    {
        ValidateId(id);
        return new PanelTabGroup(id, [new PanelTab(id, ValidateName(name))], id);
    }

    public static PanelTabGroup Restore(PanelInstanceId groupId, IEnumerable<PanelTab> tabs, PanelInstanceId activeId)
    {
        ValidateId(groupId);
        ArgumentNullException.ThrowIfNull(tabs);
        var copy = tabs.ToImmutableArray();
        if (copy.IsEmpty) throw new ArgumentException(UiText.Get("APanelMustHaveAtLeastOne"), nameof(tabs));
        var ids = new HashSet<PanelInstanceId>();
        foreach (var tab in copy)
        {
            if (tab is null) throw new ArgumentException(UiText.Get("InvalidTab"), nameof(tabs));
            ValidateId(tab.InstanceId);
            _ = ValidateName(tab.Name);
            if (!ids.Add(tab.InstanceId)) throw new ArgumentException(UiText.Get("DuplicateTabId"), nameof(tabs));
        }
        if (!ids.Contains(activeId)) throw new ArgumentException(UiText.Get("TheActiveTabDoesNotBelongTo"), nameof(activeId));
        return new PanelTabGroup(groupId, copy, activeId);
    }

    public PanelTabGroup Add(PanelInstanceId id, string name)
    {
        ValidateId(id);
        if (Tabs.Any(tab => tab.InstanceId == id)) throw new ArgumentException(UiText.Get("TheTabIdAlreadyExists"), nameof(id));
        return new PanelTabGroup(GroupId, Tabs.Add(new PanelTab(id, ValidateName(name))), ActiveId);
    }

    public PanelTabGroup Rename(PanelInstanceId id, string name)
    {
        var index = Find(id);
        return new PanelTabGroup(GroupId, Tabs.SetItem(index, Tabs[index] with { Name = ValidateName(name) }), ActiveId);
    }

    public PanelTabGroup Select(PanelInstanceId id)
    {
        _ = Find(id);
        return new PanelTabGroup(GroupId, Tabs, id);
    }

    public PanelTabGroup Move(PanelInstanceId id, int index)
    {
        var oldIndex = Find(id);
        if (index < 0 || index >= Tabs.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (oldIndex == index) return this;
        return new PanelTabGroup(GroupId, Tabs.RemoveAt(oldIndex).Insert(index, Tabs[oldIndex]), ActiveId);
    }

    public PanelTabGroup Remove(PanelInstanceId id)
    {
        var index = Find(id);
        if (Tabs.Length == 1) throw new InvalidOperationException(UiText.Get("TheLastTabCannotBeDeleted"));
        var remaining = Tabs.RemoveAt(index);
        var active = id == ActiveId ? remaining[Math.Max(0, index - 1)].InstanceId : ActiveId;
        return new PanelTabGroup(GroupId, remaining, active);
    }

    private int Find(PanelInstanceId id)
    {
        for (var index = 0; index < Tabs.Length; index++)
            if (Tabs[index].InstanceId == id) return index;
        throw new ArgumentException(UiText.Get("TheTabWasNotFound"), nameof(id));
    }

    private static void ValidateId(PanelInstanceId id)
    {
        if (id.IsEmpty) throw new ArgumentException(UiText.Get("TheTabIdCannotBeEmpty"), nameof(id));
    }

    private static string ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new ArgumentException(UiText.Get("TheTabNameCannotBeEmpty"), nameof(name)) : name.Trim();
}
