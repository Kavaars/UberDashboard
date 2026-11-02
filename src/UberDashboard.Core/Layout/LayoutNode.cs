namespace UberDashboard.Core.Layout;

/// <summary>
/// Węzeł układu. Węzeł jest wyłącznie liściem (<see cref="LeafNode"/>) albo podziałem
/// (<see cref="SplitNode"/>); nie istnieje trzeci wariant.
/// </summary>
public abstract record LayoutNode
{
    /// <summary>Liczba liści w poddrzewie.</summary>
    public abstract int LeafCount { get; }

    /// <summary>Głębokość poddrzewa; liść ma głębokość 1.</summary>
    public abstract int Depth { get; }
}

/// <summary>
/// Liść wskazuje wyłącznie <see cref="PanelInstanceId"/>, nigdy typ panelu.
/// Dzięki temu dwie instancje tego samego typu mają niezależny stan i pozycję.
/// </summary>
public sealed record LeafNode(PanelInstanceId InstanceId) : LayoutNode
{
    /// <inheritdoc />
    public override int LeafCount => 1;

    /// <inheritdoc />
    public override int Depth => 1;
}

/// <summary>
/// Podział na dwie części. W układzie poziomym <see cref="First"/> jest po lewej,
/// w pionowym u góry. <see cref="Ratio"/> opisuje udział <see cref="First"/>.
/// </summary>
public sealed record SplitNode(Guid SplitId, SplitAxis Axis, double Ratio, LayoutNode First, LayoutNode Second) : LayoutNode
{
    /// <summary>Tworzy podział z wyliczonym identyfikatorem.</summary>
    public static SplitNode Create(SplitAxis axis, double ratio, LayoutNode first, LayoutNode second) =>
        new(Guid.NewGuid(), axis, ratio, first, second);

    /// <inheritdoc />
    public override int LeafCount => First.LeafCount + Second.LeafCount;

    /// <inheritdoc />
    public override int Depth => 1 + Math.Max(First.Depth, Second.Depth);
}
