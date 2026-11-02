namespace UberDashboard.Core.Layout;

/// <summary>
/// Operacje domenowe na drzewie układu. Wszystkie metody są czystymi funkcjami:
/// przyjmują układ, zwracają nowy układ albo wynik nieudany z układem wejściowym.
/// </summary>
public static class LayoutOperations
{
    /// <summary>
    /// Dzieli liść wskazanej instancji na dwa liście. Nowa instancja trafia przed
    /// albo za istniejący liść zależnie od <paramref name="side"/>.
    /// </summary>
    public static LayoutOperationResult Split(
        LayoutTree tree,
        PanelInstanceId target,
        SplitAxis axis,
        PanelInstanceId newInstance,
        InsertSide side)
    {
        if (tree.Root is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.MissingRoot, "Nie można dzielić pustego układu.");
        }

        if (LayoutWalker.FindLeaf(tree.Root, target) is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, $"Nie znaleziono panelu {target}.");
        }

        if (LayoutWalker.Contains(tree.Root, newInstance))
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.DuplicateInstance, $"Instancja {newInstance} już występuje w układzie.");
        }

        var found = false;
        var newRoot = MapLeaf(tree.Root, target, _ => found = true, () => BuildSplit(axis, target, newInstance, side));
        if (!found)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, $"Nie znaleziono panelu {target}.");
        }

        return LayoutOperationResult.Ok(tree.WithRoot(newRoot));
    }

    /// <summary>
    /// Przenosi liść w miejsce wskazywane przez strefę upuszczenia na innym liściu.
    /// Przeniesienie na samego siebie jest odrzucane.
    /// </summary>
    public static LayoutOperationResult Move(
        LayoutTree tree,
        PanelInstanceId instance,
        PanelInstanceId target,
        DropZone dropZone)
    {
        if (tree.Root is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.MissingRoot, "Nie można przenosić w pustym układzie.");
        }

        if (instance == target)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.SelfMove, "Panel nie może być przeniesiony na samego siebie.");
        }

        var (axis, side) = AxisAndSideFor(dropZone);
        var removed = false;
        var afterRemoval = RemoveNode(tree.Root, instance, ref removed);

        if (!removed)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, $"Nie znaleziono panelu {instance}.");
        }

        if (afterRemoval is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.InvalidOperation, "Po usunięciu panelu układ jest pusty.");
        }

        if (LayoutWalker.FindLeaf(afterRemoval, target) is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, $"Nie znaleziono celu przeniesienia {target}.");
        }

        var found = false;
        var newRoot = MapLeaf(afterRemoval, target, _ => found = true, () => BuildSplit(axis, target, instance, side));
        if (!found)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, $"Nie znaleziono celu przeniesienia {target}.");
        }

        return LayoutOperationResult.Ok(tree.WithRoot(newRoot));
    }

    /// <summary>
    /// Zamienia dwa liście miejscami. Dane instancji pozostają przy swoim
    /// <see cref="PanelInstanceId"/> — przenoszona jest wyłącznie pozycja w układzie.
    /// </summary>
    public static LayoutOperationResult Swap(LayoutTree tree, PanelInstanceId first, PanelInstanceId second)
    {
        if (tree.Root is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.MissingRoot, "Nie można zamieniać w pustym układzie.");
        }

        if (first == second)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.SelfMove, "Zamiana wymaga dwóch różnych instancji.");
        }

        var hits = 0;
        var newRoot = SwapNodes(tree.Root, first, second, ref hits);

        return hits == 2
            ? LayoutOperationResult.Ok(tree.WithRoot(newRoot))
            : LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, "Obie instancje muszą występować w układzie.");
    }

    /// <summary>
    /// Wstawia nową instancję w miejsce dotychczasowej. Poprzednia instancja
    /// nie jest usuwana — aplikacja decyduje, czy ją ukryć, czy usunąć.
    /// </summary>
    public static LayoutOperationResult Replace(LayoutTree tree, PanelInstanceId instance, PanelInstanceId replacement)
    {
        if (tree.Root is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.MissingRoot, "Nie można zamieniać zawartości pustego układu.");
        }

        if (LayoutWalker.Contains(tree.Root, replacement))
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.DuplicateInstance, $"Instancja {replacement} już występuje w układzie.");
        }

        var found = false;
        var newRoot = MapLeaf(tree.Root, instance, _ => found = true, () => new LeafNode(replacement));

        return found
            ? LayoutOperationResult.Ok(tree.WithRoot(newRoot))
            : LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, $"Nie znaleziono panelu {instance}.");
    }

    /// <summary>
    /// Usuwa liść z układu i kompaktuje podziały z jednym dzieckiem.
    /// Operacja jest wspólna dla ukrycia i usunięcia instancji.
    /// </summary>
    public static LayoutOperationResult Remove(LayoutTree tree, PanelInstanceId instance)
    {
        if (tree.Root is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.MissingRoot, "Układ jest już pusty.");
        }

        var removed = false;
        var newRoot = RemoveNode(tree.Root, instance, ref removed);

        return removed
            ? LayoutOperationResult.Ok(tree.WithRoot(newRoot))
            : LayoutOperationResult.Fail(tree, LayoutErrorCode.LeafNotFound, $"Nie znaleziono panelu {instance}.");
    }

    /// <summary>Zmienia oś podziału, zachowując kolejność dzieci i proporcję.</summary>
    public static LayoutOperationResult ToggleAxis(LayoutTree tree, Guid splitId)
    {
        if (tree.Root is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.MissingRoot, "Układ jest pusty.");
        }

        var found = false;
        var newRoot = MapSplit(tree.Root, splitId, split => split with
        {
            Axis = split.Axis == SplitAxis.Horizontal ? SplitAxis.Vertical : SplitAxis.Horizontal,
        }, ref found);

        return found
            ? LayoutOperationResult.Ok(tree.WithRoot(newRoot))
            : LayoutOperationResult.Fail(tree, LayoutErrorCode.SplitNotFound, $"Nie znaleziono podziału {splitId}.");
    }

    /// <summary>
    /// Ustawia proporcję podziału, zaciskając ją do przedziału spełniającego minima
    /// obu poddrzew. <paramref name="regionLength"/> to długość obszaru podziału
    /// w osi podziału, wyrażona w DIP.
    /// </summary>
    public static LayoutOperationResult Resize(
        LayoutTree tree,
        Guid splitId,
        double ratio,
        double regionLength,
        IPanelMinSizeSource minSizes,
        LayoutMetrics metrics)
    {
        if (tree.Root is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.MissingRoot, "Układ jest pusty.");
        }

        var split = LayoutWalker.FindSplit(tree.Root, splitId);
        if (split is null)
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.SplitNotFound, $"Nie znaleziono podziału {splitId}.");
        }

        var axisAvailable = regionLength - metrics.SplitterThickness;
        var minFirst = MinimumSizes.Of(split.First, minSizes, metrics).Along(split.Axis);
        var minSecond = MinimumSizes.Of(split.Second, minSizes, metrics).Along(split.Axis);

        var clamped = LayoutSolver.TryClampRatio(ratio, minFirst, minSecond, axisAvailable);
        if (clamped is null)
        {
            return LayoutOperationResult.Fail(
                tree,
                axisAvailable <= 0d ? LayoutErrorCode.InsufficientSpace : LayoutErrorCode.InvalidRatio,
                axisAvailable <= 0d
                    ? "Obszar podziału jest zbyt mały, aby zapisać proporcję."
                    : "Nie można zapisać proporcji niezgodnej z minimalnymi rozmiarami paneli.");
        }

        var found = false;
        var newRoot = MapSplit(tree.Root, splitId, node => node with { Ratio = clamped.Value }, ref found);

        return found
            ? LayoutOperationResult.Ok(tree.WithRoot(newRoot))
            : LayoutOperationResult.Fail(tree, LayoutErrorCode.SplitNotFound, $"Nie znaleziono podziału {splitId}.");
    }

    /// <summary>
    /// Przywraca ukryty panel. Najpierw próbuje zapamiętanego sąsiada, potem
    /// kolejnych kandydatów zapasowych, a na końcu dokłada panel przy korzeniu.
    /// </summary>
    public static LayoutOperationResult Restore(LayoutTree tree, PanelInstanceId instance, RestoreHint hint)
    {
        if (LayoutWalker.Contains(tree.Root, instance))
        {
            return LayoutOperationResult.Fail(tree, LayoutErrorCode.DuplicateInstance, $"Instancja {instance} już występuje w układzie.");
        }

        if (tree.Root is null)
        {
            return LayoutOperationResult.Ok(tree.WithRoot(new LeafNode(instance)));
        }

        var side = hint.WasFirstChild ? InsertSide.Before : InsertSide.After;

        if (hint.Sibling is { } sibling && LayoutWalker.Contains(tree.Root, sibling))
        {
            return Split(tree, sibling, hint.Axis, instance, side);
        }

        foreach (var candidate in hint.FallbackCandidates)
        {
            if (LayoutWalker.Contains(tree.Root, candidate))
            {
                var fallback = Split(tree, candidate, hint.Axis, instance, InsertSide.After);
                return fallback.IsSuccess
                    ? fallback with { UsedFallback = true }
                    : fallback;
            }
        }

        var anchor = LayoutWalker.FirstLeaf(tree.Root);
        if (anchor is null)
        {
            return LayoutOperationResult.Ok(tree.WithRoot(new LeafNode(instance)));
        }

        var appended = Split(tree, anchor.InstanceId, hint.Axis, instance, InsertSide.After);
        return appended.IsSuccess
            ? appended with { UsedFallback = true }
            : appended;
    }

    private static (SplitAxis Axis, InsertSide Side) AxisAndSideFor(DropZone dropZone) => dropZone switch
    {
        DropZone.Left => (SplitAxis.Horizontal, InsertSide.Before),
        DropZone.Right => (SplitAxis.Horizontal, InsertSide.After),
        DropZone.Top => (SplitAxis.Vertical, InsertSide.Before),
        _ => (SplitAxis.Vertical, InsertSide.After),
    };

    private static LayoutNode BuildSplit(SplitAxis axis, PanelInstanceId existing, PanelInstanceId inserted, InsertSide side) =>
        side == InsertSide.Before
            ? SplitNode.Create(axis, 0.5d, new LeafNode(inserted), new LeafNode(existing))
            : SplitNode.Create(axis, 0.5d, new LeafNode(existing), new LeafNode(inserted));

    private static LayoutNode MapLeaf(
        LayoutNode node,
        PanelInstanceId target,
        Action<LeafNode> onFound,
        Func<LayoutNode> replacement)
    {
        switch (node)
        {
            case LeafNode leaf when leaf.InstanceId == target:
                onFound(leaf);
                return replacement();

            case LeafNode leaf:
                return leaf;

            case SplitNode split:
            {
                var first = MapLeaf(split.First, target, onFound, replacement);
                var second = MapLeaf(split.Second, target, onFound, replacement);

                return ReferenceEquals(first, split.First) && ReferenceEquals(second, split.Second)
                    ? split
                    : split with { First = first, Second = second };
            }

            default:
                return node;
        }
    }

    private static LayoutNode? RemoveNode(LayoutNode node, PanelInstanceId instance, ref bool removed)
    {
        switch (node)
        {
            case LeafNode leaf when leaf.InstanceId == instance:
                removed = true;
                return null;

            case LeafNode leaf:
                return leaf;

            case SplitNode split:
            {
                var first = RemoveNode(split.First, instance, ref removed);
                var second = RemoveNode(split.Second, instance, ref removed);

                if (first is null && second is null)
                {
                    return null;
                }

                if (first is null)
                {
                    return second;
                }

                if (second is null)
                {
                    return first;
                }

                return split with { First = first, Second = second };
            }

            default:
                return node;
        }
    }

    private static LayoutNode SwapNodes(LayoutNode node, PanelInstanceId first, PanelInstanceId second, ref int hits)
    {
        switch (node)
        {
            case LeafNode leaf when leaf.InstanceId == first:
                hits++;
                return new LeafNode(second);

            case LeafNode leaf when leaf.InstanceId == second:
                hits++;
                return new LeafNode(first);

            case LeafNode leaf:
                return leaf;

            case SplitNode split:
                return split with
                {
                    First = SwapNodes(split.First, first, second, ref hits),
                    Second = SwapNodes(split.Second, first, second, ref hits),
                };

            default:
                return node;
        }
    }

    private static LayoutNode MapSplit(LayoutNode node, Guid splitId, Func<SplitNode, LayoutNode> transform, ref bool found)
    {
        switch (node)
        {
            case LeafNode leaf:
                return leaf;

            case SplitNode split:
            {
                var first = MapSplit(split.First, splitId, transform, ref found);
                var second = MapSplit(split.Second, splitId, transform, ref found);

                if (split.SplitId == splitId)
                {
                    found = true;
                    return transform(split with { First = first, Second = second });
                }

                return ReferenceEquals(first, split.First) && ReferenceEquals(second, split.Second)
                    ? split
                    : split with { First = first, Second = second };
            }

            default:
                return node;
        }
    }
}
