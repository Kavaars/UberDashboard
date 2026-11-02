namespace UberDashboard.Core.Layout;

/// <summary>
/// Źródło minimalnego rozmiaru panelu. Implementacje pochodzą z rejestru typów
/// paneli (host, wtyczki) albo ze słownika w testach.
/// </summary>
public interface IPanelMinSizeSource
{
    /// <summary>Minimalny rozmiar instancji w DIP.</summary>
    MinSize MinimumOf(PanelInstanceId instanceId);
}

/// <summary>Rejestr minimów oparty na słowniku; używany przez host i w testach.</summary>
public sealed class PanelMinSizeRegistry : IPanelMinSizeSource
{
    private readonly Dictionary<PanelInstanceId, MinSize> _sizes = [];
    private readonly MinSize _fallback;

    /// <summary>Tworzy rejestr z opcjonalnym minimum domyślnym dla nieznanych instancji.</summary>
    public PanelMinSizeRegistry(MinSize? fallback = null) => _fallback = fallback ?? MinSize.Zero;

    /// <summary>Ustawia minimum wskazanej instancji.</summary>
    public void Set(PanelInstanceId instanceId, MinSize size) => _sizes[instanceId] = size;

    /// <summary>Usuwa wpis instancji.</summary>
    public bool Remove(PanelInstanceId instanceId) => _sizes.Remove(instanceId);

    /// <inheritdoc />
    public MinSize MinimumOf(PanelInstanceId instanceId) =>
        _sizes.TryGetValue(instanceId, out var size) ? size : _fallback;
}

/// <summary>Oblicza minimum całych poddrzew zgodnie z osią podziału.</summary>
public static class MinimumSizes
{
    /// <summary>
    /// Minimum grupy. W osi dzielenia sumuje minima dzieci i grubość dzielnika,
    /// w drugiej osi wybiera większe minimum.
    /// </summary>
    public static MinSize Of(LayoutNode node, IPanelMinSizeSource source, LayoutMetrics metrics) =>
        OfCore(node, source, metrics, 1);

    /// <summary>Minimum całego układu.</summary>
    public static MinSize Of(LayoutTree tree, IPanelMinSizeSource source, LayoutMetrics metrics) =>
        tree.Root is null ? MinSize.Zero : Of(tree.Root, source, metrics);

    private static MinSize OfCore(LayoutNode node, IPanelMinSizeSource source, LayoutMetrics metrics, int depth)
    {
        if (depth > LayoutLimits.Default.MaxDepth)
        {
            return MinSize.Zero;
        }

        switch (node)
        {
            case LeafNode leaf:
                return source.MinimumOf(leaf.InstanceId);

            case SplitNode split:
            {
                var first = OfCore(split.First, source, metrics, depth + 1);
                var second = OfCore(split.Second, source, metrics, depth + 1);

                return split.Axis == SplitAxis.Horizontal
                    ? new MinSize(first.Width + metrics.SplitterThickness + second.Width, Math.Max(first.Height, second.Height))
                    : new MinSize(Math.Max(first.Width, second.Width), first.Height + metrics.SplitterThickness + second.Height);
            }

            default:
                return MinSize.Zero;
        }
    }
}
