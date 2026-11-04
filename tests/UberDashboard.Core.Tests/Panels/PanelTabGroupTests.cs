using UberDashboard.Core.Layout;
using UberDashboard.Core.Panels;

namespace UberDashboard.Core.Tests.Panels;

public sealed class PanelTabGroupTests
{
    [Fact]
    public void Add_move_rename_and_select_preserve_identities()
    {
        var first = PanelInstanceId.New();
        var second = PanelInstanceId.New();
        var group = PanelTabGroup.Create(first, "Pierwsza").Add(second, "Druga").Move(second, 0).Rename(second, "Nowa").Select(second);
        Assert.Equal([second, first], group.Tabs.Select(tab => tab.InstanceId));
        Assert.Equal("Nowa", group.Tabs[0].Name);
        Assert.Equal(second, group.ActiveId);
        Assert.Equal(first, group.GroupId);
    }

    [Fact]
    public void Removing_active_tab_selects_neighbour_and_last_tab_is_protected()
    {
        var first = PanelInstanceId.New();
        var second = PanelInstanceId.New();
        var group = PanelTabGroup.Create(first, "Jeden").Add(second, "Dwa").Select(second).Remove(second);
        Assert.Equal(first, group.ActiveId);
        Assert.Throws<InvalidOperationException>(() => group.Remove(first));
    }

    [Fact]
    public void Restore_validates_persisted_group_and_makes_a_copy()
    {
        var groupId = PanelInstanceId.New();
        var first = PanelInstanceId.New();
        var second = PanelInstanceId.New();
        var input = new List<PanelTab> { new(first, "Jeden"), new(second, "Dwa") };
        var group = PanelTabGroup.Restore(groupId, input, second);
        input.Clear();
        Assert.Equal(2, group.Tabs.Length);
        Assert.Equal(second, group.ActiveId);
        Assert.Throws<ArgumentException>(() => PanelTabGroup.Restore(groupId, [new(first, "Jeden"), new(first, "Dwa")], first));
        Assert.Throws<ArgumentException>(() => PanelTabGroup.Restore(groupId, [new(first, "Jeden")], second));
        Assert.Throws<ArgumentException>(() => PanelTabGroup.Restore(groupId, [], first));
    }

    [Fact]
    public void Invalid_ids_names_and_positions_are_rejected()
    {
        var id = PanelInstanceId.New();
        var group = PanelTabGroup.Create(id, "Jeden");
        Assert.Throws<ArgumentException>(() => PanelTabGroup.Create(PanelInstanceId.Empty, "Jeden"));
        Assert.Throws<ArgumentException>(() => group.Add(id, "Drugi"));
        Assert.Throws<ArgumentException>(() => group.Rename(id, "  "));
        Assert.Throws<ArgumentOutOfRangeException>(() => group.Move(id, 1));
        Assert.Throws<ArgumentException>(() => group.Select(PanelInstanceId.New()));
    }
}
