namespace UberDashboard.Core.Layout;

/// <summary>Umieszczenie panelu w przestrzeni roboczej, wyrażone w DIP.</summary>
public sealed record PanelPlacement(PanelInstanceId InstanceId, LayoutRect Bounds);

/// <summary>Wynik rozwiązania układu dla zadanego obszaru.</summary>
public sealed record LayoutSolution(IReadOnlyList<PanelPlacement> Placements, bool IsOverflowing)
{
    /// <summary>Puste rozwiązanie bez umieszczeń.</summary>
    public static LayoutSolution Empty { get; } = new([], false);

    /// <summary>Umieszczenie wskazanej instancji albo <see langword="null"/>.</summary>
    public LayoutRect? BoundsOf(PanelInstanceId instanceId)
    {
        foreach (var placement in Placements)
        {
            if (placement.InstanceId == instanceId)
            {
                return placement.Bounds;
            }
        }

        return null;
    }
}

/// <summary>
/// Rozwiązuje drzewo układu do prostokątów w DIP. Algorytm jest jednoprzebiegowy,
/// iteracyjny i respektuje minima potomków; gdy obszar jest za mały, ustawia
/// <see cref="LayoutSolution.IsOverflowing"/> zamiast po cichu ucinać treść.
/// </summary>
public static class LayoutSolver
{
    /// <summary>Rozwiązuje układ w zadanym prostokącie.</summary>
    public static LayoutSolution Solve(
        LayoutTree tree,
        LayoutRect available,
        IPanelMinSizeSource minSizes,
        LayoutMetrics metrics)
    {
        if (tree.Root is null)
        {
            return LayoutSolution.Empty;
        }

        var placements = new List<PanelPlacement>(Math.Max(1, tree.LeafCount));
        var overflow = false;
        var stack = new Stack<(LayoutNode Node, LayoutRect Bounds)>();
        stack.Push((tree.Root, available));

        while (stack.Count > 0)
        {
            var (node, bounds) = stack.Pop();

            switch (node)
            {
                case LeafNode leaf:
                {
                    var minimum = minSizes.MinimumOf(leaf.InstanceId);
                    if (bounds.Width < minimum.Width || bounds.Height < minimum.Height)
                    {
                        overflow = true;
                    }

                    placements.Add(new PanelPlacement(leaf.InstanceId, bounds));
                    break;
                }

                case SplitNode split:
                {
                    var axis = split.Axis;
                    var firstMin = MinimumSizes.Of(split.First, minSizes, metrics);
                    var secondMin = MinimumSizes.Of(split.Second, minSizes, metrics);

                    var crossLimit = Math.Max(
                        axis == SplitAxis.Horizontal ? firstMin.Height : firstMin.Width,
                        axis == SplitAxis.Horizontal ? secondMin.Height : secondMin.Width);

                    if (crossLimit > bounds.LengthAlong(axis == SplitAxis.Horizontal ? SplitAxis.Vertical : SplitAxis.Horizontal))
                    {
                        overflow = true;
                    }

                    var axisAvailable = bounds.LengthAlong(axis) - metrics.SplitterThickness;
                    var minFirst = firstMin.Along(axis);
                    var minSecond = secondMin.Along(axis);
                    double ratio;

                    if (axisAvailable <= 0d)
                    {
                        overflow = true;
                        ratio = 0.5d;
                    }
                    else if (axisAvailable < minFirst + minSecond)
                    {
                        // Obszar mniejszy niż suma minimów: rozdzielamy proporcjonalnie do minimów,
                        // zgłaszamy przepełnienie i nie zapisujemy takiej proporcji w dokumencie.
                        overflow = true;
                        var sum = minFirst + minSecond;
                        ratio = sum > 0d ? minFirst / sum : 0.5d;
                    }
                    else
                    {
                        ratio = ClampRatio(split.Ratio, minFirst, minSecond, axisAvailable);
                    }

                    var firstLength = ratio * axisAvailable;
                    var (firstBounds, secondBounds, _) = bounds.SplitAlong(axis, firstLength, metrics.SplitterThickness);

                    stack.Push((split.Second, secondBounds));
                    stack.Push((split.First, firstBounds));
                    break;
                }

                default:
                    break;
            }
        }

        return new LayoutSolution(placements, overflow);
    }

    /// <summary>
    /// Zaciska proporcję do przedziału spełniającego minima obu poddrzew.
    /// Zwraca <see langword="null"/>, gdy dostępna długość jest niedodatnia.
    /// </summary>
    public static double? TryClampRatio(double ratio, double minFirst, double minSecond, double availableLength)
    {
        if (!double.IsFinite(ratio) || availableLength <= 0d)
        {
            return null;
        }

        if (availableLength < minFirst + minSecond)
        {
            return null;
        }

        return ClampRatio(ratio, minFirst, minSecond, availableLength);
    }

    private static double ClampRatio(double ratio, double minFirst, double minSecond, double availableLength)
    {
        var lower = minFirst / availableLength;
        var upper = 1d - (minSecond / availableLength);

        if (upper < lower)
        {
            return lower;
        }

        var safeRatio = double.IsFinite(ratio) ? ratio : 0.5d;
        return Math.Clamp(safeRatio, lower, upper);
    }
}
