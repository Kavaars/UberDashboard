using UberDashboard.App.Shell;
using UberDashboard.Core.Layout;
using Fixture = UberDashboard.App.Tests.Shell.ShellPersistenceRegressionTests.ShellFixture;
namespace UberDashboard.App.Tests.Shell;
public sealed class PanelSpaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Edge_creates_persistent_space_and_reuses_existing_boundary(bool top)
    {
        await using var f = new Fixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var original = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        await f.Shell.ReserveSpaceAsync(original.InstanceId, top, 200, 800);
        Assert.Same(original, f.Shell.Root);
        f.Shell.IsEditMode = true;
        await f.Shell.ReserveSpaceAsync(original.InstanceId, top, 200, 800);
        var split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(SplitAxis.Vertical, split.Axis);
        Assert.True(Assert.IsType<PanelLeafViewModel>(top ? split.First : split.Second).IsSpace);
        var ratio = split.Ratio;
        await f.Shell.ReserveSpaceAsync(original.InstanceId, top, 40, 594);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.IsType<PanelLeafViewModel>(split.First); Assert.IsType<PanelLeafViewModel>(split.Second);
        Assert.True(top ? split.Ratio > ratio : split.Ratio < ratio);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        var space = Assert.IsType<PanelLeafViewModel>(top ? split.First : split.Second);
        await f.Shell.AddPanelAtAsync("notes", space.InstanceId, SplitAxis.Vertical);
        split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal("notes", Assert.IsType<PanelLeafViewModel>(top ? split.First : split.Second).PanelTypeId);
        Assert.Empty(f.Shell.HiddenPanels);
        Assert.DoesNotContain(f.Shell.PanelTypes, t => t.TypeId == UberDashboard.App.Features.Space.SpacePanelDescriptor.Id);
    }
    [Fact]
    public async Task Dropping_into_space_fills_it_and_keeps_source_identity()
    {
        await using var f = new Fixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var original = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        var todo = Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(original.ViewModel);
        todo.NewItemText = "Zachowana treść";
        todo.AddItemCommand.Execute(null);
        f.Shell.IsEditMode = true;
        await f.Shell.ReserveSpaceAsync(original.InstanceId, false, 200, 800);
        var space = Assert.IsType<PanelLeafViewModel>(Assert.IsType<PanelSplitViewModel>(f.Shell.Root).Second);
        await f.Shell.MovePanelAsync(original.InstanceId, space.InstanceId, DropZone.Bottom);
        Assert.Equal(original.InstanceId, Assert.IsType<PanelLeafViewModel>(f.Shell.Root).InstanceId);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(original.InstanceId, Assert.IsType<PanelLeafViewModel>(f.Shell.Root).InstanceId);
        Assert.Empty(f.Shell.HiddenPanels);
        Assert.Equal("Zachowana treść", Assert.Single(Assert.IsType<UberDashboard.App.Features.Todo.TodoPanelViewModel>(Assert.IsType<PanelLeafViewModel>(f.Shell.Root).ViewModel).Items).Text);
    }
    [Fact]
    public async Task Invalid_or_insufficient_height_does_not_change_layout()
    {
        await using var f = new Fixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root); f.Shell.IsEditMode = true;
        await f.Shell.ReserveSpaceAsync(leaf.InstanceId, false, double.NaN, 800);
        await f.Shell.ReserveSpaceAsync(leaf.InstanceId, false, 80, 160);
        Assert.Same(leaf, f.Shell.Root);
    }

    [Fact]
    public async Task Failed_space_layout_save_can_be_retried_without_duplicating_panels()
    {
        await using var f = new Fixture();
        await f.Shell.InitializeAsync(); await f.Shell.AddPanelCommand.ExecuteAsync("todo");
        var leaf = Assert.IsType<PanelLeafViewModel>(f.Shell.Root);
        f.Shell.IsEditMode = true; f.LayoutFailure.Fail = true;
        await f.Shell.ReserveSpaceAsync(leaf.InstanceId, false, 160, 600);
        Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        f.LayoutFailure.Fail = false;
        await f.Shell.RetrySaveLayoutCommand.ExecuteAsync(null);
        await f.Shell.ReloadCommand.ExecuteAsync(null);
        var split = Assert.IsType<PanelSplitViewModel>(f.Shell.Root);
        Assert.Equal(leaf.InstanceId, Assert.IsType<PanelLeafViewModel>(split.First).InstanceId);
        Assert.True(Assert.IsType<PanelLeafViewModel>(split.Second).IsSpace);
    }
}
