using Microsoft.Extensions.Logging.Abstractions;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Layout;
using UberDashboard.Application.Tests.TestSupport;
using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Tests.Layout;

public sealed class LayoutEditorTests
{
    private static LayoutEditor CreateEditor(out FakeLayoutStore store, out PanelMinSizeRegistry minSizes, LayoutTree? initial = null)
    {
        store = new FakeLayoutStore();
        minSizes = new PanelMinSizeRegistry();
        if (initial is not null)
        {
            store.Seed(initial);
        }

        return new LayoutEditor(store, minSizes, NullLogger<LayoutEditor>.Instance);
    }

    private static PanelInstanceId Id(int value)
    {
        var bytes = new byte[8];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new PanelInstanceId(new Guid(value, 0, 0, bytes));
    }

    private static LayoutTree SingleLeaf(int id) => new(new LeafNode(Id(id)));

    private static LayoutTree Split(int first, int second) =>
        new(SplitNode.Create(SplitAxis.Horizontal, 0.5d, new LeafNode(Id(first)), new LeafNode(Id(second))));

    [Fact]
    public async Task Load_sets_tree_and_clears_dirty()
    {
        var editor = CreateEditor(out _, out _, Split(1, 2));

        var result = await editor.LoadAsync(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, editor.Tree.LeafCount);
        Assert.False(editor.IsDirty);
        Assert.Equal(LayoutErrorCode.None, editor.LastLoadError);
    }

    [Fact]
    public async Task Load_failure_preserves_original_document_flag()
    {
        var editor = CreateEditor(out var store, out _);
        store.ForceLoadResult(LayoutLoadResult.Fail(LayoutErrorCode.UnknownSchemaVersion, 99, "nowsza wersja", preserve: true));

        var result = await editor.LoadAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.True(editor.OriginalDocumentPreserved);
        Assert.Equal(LayoutErrorCode.UnknownSchemaVersion, editor.LastLoadError);
        Assert.True(editor.Tree.IsEmpty);
    }

    [Fact]
    public async Task Split_marks_dirty_and_changes_tree()
    {
        var editor = CreateEditor(out _, out _, SingleLeaf(1));
        await editor.LoadAsync(Guid.NewGuid());

        var result = editor.Split(Id(1), SplitAxis.Vertical, Id(2), InsertSide.After);

        Assert.True(result.IsSuccess);
        Assert.True(editor.IsDirty);
        Assert.Equal([Id(1), Id(2)], LayoutWalker.Instances(editor.Tree.Root));
    }

    [Fact]
    public async Task Split_of_unknown_target_keeps_tree_unchanged()
    {
        var editor = CreateEditor(out _, out _, SingleLeaf(1));
        await editor.LoadAsync(Guid.NewGuid());
        var before = editor.Tree;

        var result = editor.Split(Id(9), SplitAxis.Vertical, Id(2), InsertSide.After);

        Assert.False(result.IsSuccess);
        Assert.Equal(LayoutErrorCode.LeafNotFound, result.Error);
        Assert.Same(before, editor.Tree);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Save_persists_only_when_dirty()
    {
        var editor = CreateEditor(out var store, out _, SingleLeaf(1));
        await editor.LoadAsync(Guid.NewGuid());
        editor.SetVisibleInstances([Id(1)]);

        var cleanSave = await editor.SaveAsync();
        Assert.True(cleanSave.IsSuccess);
        Assert.Equal(0, store.SaveCount);

        editor.Split(Id(1), SplitAxis.Horizontal, Id(2), InsertSide.After);
        editor.SetVisibleInstances([Id(1), Id(2)]);

        var dirtySave = await editor.SaveAsync();
        Assert.True(dirtySave.IsSuccess);
        Assert.Equal(1, store.SaveCount);
        Assert.False(editor.IsDirty);
        Assert.Equal([Id(1), Id(2)], LayoutWalker.Instances(store.LastSaved.Root));
    }

    [Fact]
    public async Task Save_rejects_layout_inconsistent_with_visible_instances()
    {
        var editor = CreateEditor(out var store, out _, SingleLeaf(1));
        await editor.LoadAsync(Guid.NewGuid());
        editor.SetVisibleInstances([]);

        var result = await editor.SaveAsync(force: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task Remove_compacts_and_save_round_trips()
    {
        var editor = CreateEditor(out var store, out _, Split(1, 2));
        await editor.LoadAsync(Guid.NewGuid());

        var remove = editor.Remove(Id(1));
        Assert.True(remove.IsSuccess);
        editor.SetVisibleInstances([Id(2)]);

        var save = await editor.SaveAsync();
        Assert.True(save.IsSuccess);
        Assert.Equal([Id(2)], LayoutWalker.Instances(store.LastSaved.Root));
    }

    [Fact]
    public async Task CaptureRestoreHint_uses_current_layout()
    {
        var editor = CreateEditor(out _, out _, Split(1, 2));
        await editor.LoadAsync(Guid.NewGuid());

        var hint = editor.CaptureRestoreHint(Id(1));

        Assert.Equal(Id(2), hint.Sibling);
        Assert.Equal(SplitAxis.Horizontal, hint.Axis);
        Assert.True(hint.WasFirstChild);
    }

    [Fact]
    public async Task Solve_reports_overflow_for_tiny_area()
    {
        var editor = CreateEditor(out _, out var minSizes, Split(1, 2));
        await editor.LoadAsync(Guid.NewGuid());
        minSizes.Set(Id(1), new MinSize(200d, 200d));
        minSizes.Set(Id(2), new MinSize(200d, 200d));

        var solution = editor.Solve(new LayoutRect(0d, 0d, 100d, 100d));

        Assert.True(solution.IsOverflowing);
        Assert.Equal(2, solution.Placements.Count);
    }

    [Fact]
    public async Task Reset_clears_state()
    {
        var editor = CreateEditor(out _, out _, Split(1, 2));
        await editor.LoadAsync(Guid.NewGuid());

        editor.Reset();

        Assert.True(editor.Tree.IsEmpty);
        Assert.Equal(Guid.Empty, editor.DashboardId);
        Assert.False(editor.IsDirty);
    }
}
