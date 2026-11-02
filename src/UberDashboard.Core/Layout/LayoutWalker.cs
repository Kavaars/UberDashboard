namespace UberDashboard.Core.Layout;

/// <summary>
/// Położenie liścia w drzewie wraz z informacją o rodzicu. Używane do budowy
/// wskazówki odtworzenia po ukryciu panelu.
/// </summary>
public sealed record LayoutLocation(
    Guid? ParentSplitId,
    SplitAxis Axis,
    bool IsFirstChild,
    double Ratio,
    IReadOnlyList<PanelInstanceId> SiblingCandidates);

/// <summary>
/// Przechodzenie po drzewie układu. Wszystkie metody są odporne na
/// <see langword="null"/> (pusty układ) i stosują limit głębokości.
/// </summary>
public static class LayoutWalker
{
    /// <summary>Wszystkie węzły w kolejności pre-order.</summary>
    public static IEnumerable<LayoutNode> Traverse(LayoutNode? root)
    {
        if (root is null)
        {
            yield break;
        }

        var stack = new Stack<LayoutNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            if (node is SplitNode split)
            {
                stack.Push(split.Second);
                stack.Push(split.First);
            }
        }
    }

    /// <summary>Wszystkie liście w kolejności pre-order.</summary>
    public static IEnumerable<LeafNode> Leaves(LayoutNode? root)
    {
        foreach (var node in Traverse(root))
        {
            if (node is LeafNode leaf)
            {
                yield return leaf;
            }
        }
    }

    /// <summary>Identyfikatory instancji w kolejności pre-order.</summary>
    public static IReadOnlyList<PanelInstanceId> Instances(LayoutNode? root)
    {
        var result = new List<PanelInstanceId>();
        foreach (var leaf in Leaves(root))
        {
            result.Add(leaf.InstanceId);
        }

        return result;
    }

    /// <summary>Pierwszy liść w kolejności pre-order lub <see langword="null"/>.</summary>
    public static LeafNode? FirstLeaf(LayoutNode? root)
    {
        foreach (var leaf in Leaves(root))
        {
            return leaf;
        }

        return null;
    }

    /// <summary>Znajduje liść wskazanej instancji.</summary>
    public static LeafNode? FindLeaf(LayoutNode? root, PanelInstanceId instanceId)
    {
        foreach (var leaf in Leaves(root))
        {
            if (leaf.InstanceId == instanceId)
            {
                return leaf;
            }
        }

        return null;
    }

    /// <summary>Znajduje podział o wskazanym identyfikatorze.</summary>
    public static SplitNode? FindSplit(LayoutNode? root, Guid splitId)
    {
        foreach (var node in Traverse(root))
        {
            if (node is SplitNode split && split.SplitId == splitId)
            {
                return split;
            }
        }

        return null;
    }

    /// <summary>Sprawdza, czy instancja występuje w układzie.</summary>
    public static bool Contains(LayoutNode? root, PanelInstanceId instanceId) => FindLeaf(root, instanceId) is not null;

    /// <summary>
    /// Wyznacza położenie liścia: rodzica, oś, stronę, proporcję oraz listę kandydatów
    /// zapasowych (najbliższy sąsiad pierwszy) używanych przy przywracaniu.
    /// </summary>
    public static LayoutLocation? Locate(LayoutNode? root, PanelInstanceId instanceId)
    {
        if (root is null)
        {
            return null;
        }

        var candidates = new List<PanelInstanceId>();
        return LocateCore(root, instanceId, null, candidates, 1);
    }

    private static LayoutLocation? LocateCore(
        LayoutNode node,
        PanelInstanceId instanceId,
        ParentDescriptor? parent,
        List<PanelInstanceId> candidates,
        int depth)
    {
        if (depth > LayoutLimits.Default.MaxDepth)
        {
            return null;
        }

        switch (node)
        {
            case LeafNode leaf when leaf.InstanceId == instanceId:
            {
                var nearestFirst = new List<PanelInstanceId>(candidates);
                nearestFirst.Reverse();
                return parent is { } descriptor
                    ? new LayoutLocation(descriptor.SplitId, descriptor.Axis, descriptor.IsFirstChild, descriptor.Ratio, nearestFirst)
                    : new LayoutLocation(null, SplitAxis.Horizontal, true, 0.5d, nearestFirst);
            }

            case LeafNode:
                return null;

            case SplitNode split:
            {
                var firstSibling = FirstLeaf(split.Second);
                if (firstSibling is not null)
                {
                    candidates.Add(firstSibling.InstanceId);
                }

                var found = LocateCore(
                    split.First,
                    instanceId,
                    new ParentDescriptor(split.SplitId, split.Axis, true, split.Ratio),
                    candidates,
                    depth + 1);

                if (firstSibling is not null)
                {
                    candidates.RemoveAt(candidates.Count - 1);
                }

                if (found is not null)
                {
                    return found;
                }

                var secondSibling = FirstLeaf(split.First);
                if (secondSibling is not null)
                {
                    candidates.Add(secondSibling.InstanceId);
                }

                found = LocateCore(
                    split.Second,
                    instanceId,
                    new ParentDescriptor(split.SplitId, split.Axis, false, split.Ratio),
                    candidates,
                    depth + 1);

                if (secondSibling is not null)
                {
                    candidates.RemoveAt(candidates.Count - 1);
                }

                return found;
            }

            default:
                return null;
        }
    }

    private readonly record struct ParentDescriptor(Guid SplitId, SplitAxis Axis, bool IsFirstChild, double Ratio);
}
