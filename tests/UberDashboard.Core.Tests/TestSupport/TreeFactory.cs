using UberDashboard.Core.Layout;

namespace UberDashboard.Core.Tests.TestSupport;

/// <summary>Pomocniki budujące deterministyczne drzewa układu w testach.</summary>
internal static class TreeFactory
{
    /// <summary>Deterministyczny identyfikator instancji dla podanej liczby.</summary>
    public static PanelInstanceId Id(int value)
    {
        var bytes = new byte[8];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new PanelInstanceId(new Guid(value, 0, 0, bytes));
    }

    /// <summary>Układ z pojedynczym liściem.</summary>
    public static LayoutTree SingleLeaf(int value) => new(new LeafNode(Id(value)));

    /// <summary>Podział z nowym, losowym identyfikatorem.</summary>
    public static SplitNode Split(SplitAxis axis, double ratio, LayoutNode first, LayoutNode second) =>
        SplitNode.Create(axis, ratio, first, second);

    /// <summary>Liść wskazanej instancji.</summary>
    public static LeafNode Leaf(int value) => new(Id(value));

    /// <summary>Rejestr minimów z wpisami <c>(instancja, minimum)</c>.</summary>
    public static PanelMinSizeRegistry Registry(params (int Instance, MinSize Size)[] entries)
    {
        var registry = new PanelMinSizeRegistry();
        foreach (var (instance, size) in entries)
        {
            registry.Set(Id(instance), size);
        }

        return registry;
    }

    /// <summary>Rejestr, który każdemu panelowi przypisuje ten sam minimalny rozmiar.</summary>
    public static UniformMinSizeSource Uniform(MinSize size) => new(size);

    /// <summary>Źródło minimów zwracające stały rozmiar dla każdej instancji.</summary>
    internal sealed class UniformMinSizeSource(MinSize size) : IPanelMinSizeSource
    {
        /// <inheritdoc />
        public MinSize MinimumOf(PanelInstanceId instanceId) => size;
    }
}
