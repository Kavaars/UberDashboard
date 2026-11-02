using FsCheck;
using FsCheck.Fluent;
using UberDashboard.Core.Layout;
using UberDashboard.Core.Layout.Serialization;

namespace UberDashboard.Core.Tests.Property;

/// <summary>
/// Testy oparte na generowaniu danych (FsCheck). Sprawdzają niezmienniki drzewa
/// układu, round-trip serializacji oraz zaciskanie proporcji do minimów.
/// </summary>
public sealed class LayoutPropertyTests
{
    private static readonly LayoutLimits Limits = LayoutLimits.Default;

    [Fact]
    public void Generated_trees_keep_invariants_and_survive_round_trip()
    {
        foreach (var shape in Gen.Sample(ShapeGen(5), 10, 200))
        {
            var tree = new LayoutTree(new Materializer().Build(shape));

            var instances = LayoutWalker.Instances(tree.Root);
            Assert.Equal(instances.Count, instances.Distinct().Count());

            var visible = new HashSet<PanelInstanceId>(instances);
            Assert.True(LayoutValidator.Validate(tree, visible, Limits).IsValid);

            var restored = LayoutSerializerRoundTrip(tree);
            Assert.Equal(tree.LeafCount, restored.LeafCount);
            AssertSameShape(tree.Root!, restored.Root!);
        }
    }

    [Fact]
    public void Resize_never_stores_ratio_outside_minimum_bounds()
    {
        var gen =
            from rawRatio in Gen.Choose(0, 1000)
            from rawMinFirst in Gen.Choose(0, 200)
            from rawMinSecond in Gen.Choose(0, 200)
            from rawAvailable in Gen.Choose(1, 600)
            select (Ratio: rawRatio / 1000d, MinFirst: (double)rawMinFirst, MinSecond: (double)rawMinSecond, Available: (double)rawAvailable);

        foreach (var sample in Gen.Sample(gen, 10, 300))
        {
            var clamped = LayoutSolver.TryClampRatio(sample.Ratio, sample.MinFirst, sample.MinSecond, sample.Available);

            if (sample.Available < sample.MinFirst + sample.MinSecond)
            {
                Assert.Null(clamped);
                continue;
            }

            Assert.NotNull(clamped);
            Assert.True(double.IsFinite(clamped!.Value));
            var lower = sample.MinFirst / sample.Available;
            var upper = 1d - (sample.MinSecond / sample.Available);
            Assert.InRange(clamped.Value, lower - 1e-9d, upper + 1e-9d);
        }
    }

    [Fact]
    public void Removing_any_generated_leaf_keeps_tree_valid()
    {
        foreach (var shape in Gen.Sample(ShapeGen(4), 10, 150))
        {
            var tree = new LayoutTree(new Materializer().Build(shape));
            var instances = LayoutWalker.Instances(tree.Root);

            foreach (var instance in instances)
            {
                var result = LayoutOperations.Remove(tree, instance);
                Assert.True(result.IsSuccess);

                var remaining = LayoutWalker.Instances(result.Tree.Root);
                Assert.Equal(instances.Count - 1, remaining.Count);
                Assert.DoesNotContain(instance, remaining);

                var visible = new HashSet<PanelInstanceId>(remaining);
                Assert.True(LayoutValidator.Validate(result.Tree, visible, Limits).IsValid);
            }
        }
    }

    private static LayoutTree LayoutSerializerRoundTrip(LayoutTree tree)
    {
        var json = LayoutSerializer.Serialize(tree, Limits);
        var result = LayoutSerializer.Deserialize(json, Limits);
        Assert.True(result.IsSuccess);
        return result.Tree!;
    }

    private static void AssertSameShape(LayoutNode expected, LayoutNode actual)
    {
        switch (expected)
        {
            case LeafNode leaf:
                var actualLeaf = Assert.IsType<LeafNode>(actual);
                Assert.Equal(leaf.InstanceId, actualLeaf.InstanceId);
                break;

            case SplitNode split:
                var actualSplit = Assert.IsType<SplitNode>(actual);
                Assert.Equal(split.SplitId, actualSplit.SplitId);
                Assert.Equal(split.Axis, actualSplit.Axis);
                Assert.Equal(split.Ratio, actualSplit.Ratio, 10);
                AssertSameShape(split.First, actualSplit.First);
                AssertSameShape(split.Second, actualSplit.Second);
                break;

            default:
                Assert.Fail("Nieznany typ węzła w teście.");
                break;
        }
    }

    private static Gen<Shape> ShapeGen(int depth) =>
        depth <= 0
            ? Gen.Constant<Shape>(new ShapeLeaf())
            : Gen.OneOf<Shape>(
            [
                Gen.Constant<Shape>(new ShapeLeaf()),
                from horizontal in Gen.Elements(new[] { true, false })
                from ratio in Gen.Choose(1, 99).Select(value => value / 100d)
                from first in ShapeGen(depth - 1)
                from second in ShapeGen(depth - 1)
                select (Shape)new ShapeSplit(horizontal, ratio, first, second),
            ]);

    private abstract record Shape;

    private sealed record ShapeLeaf : Shape;

    private sealed record ShapeSplit(bool Horizontal, double Ratio, Shape First, Shape Second) : Shape;

    private sealed class Materializer
    {
        private int _nextId = 1;

        public LayoutNode Build(Shape shape) => shape switch
        {
            ShapeLeaf => new LeafNode(NextId()),
            ShapeSplit split => new SplitNode(
                Guid.NewGuid(),
                split.Horizontal ? SplitAxis.Horizontal : SplitAxis.Vertical,
                split.Ratio,
                Build(split.First),
                Build(split.Second)),
            _ => throw new InvalidOperationException($"Nieznany kształt: {shape.GetType().Name}."),
        };

        private PanelInstanceId NextId()
        {
            var value = _nextId;
            _nextId++;
            var bytes = new byte[8];
            BitConverter.GetBytes(value).CopyTo(bytes, 0);
            return new PanelInstanceId(new Guid(value, 0, 0, bytes));
        }
    }
}
