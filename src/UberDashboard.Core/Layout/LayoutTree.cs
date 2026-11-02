namespace UberDashboard.Core.Layout;

/// <summary>
/// Dokument układu jednego pulpitu. Pusty pulpit ma jawny stan bez korzenia
/// (<see cref="Empty"/>), a nie korzeń zastępczy.
/// </summary>
public sealed record LayoutTree(LayoutNode? Root)
{
    /// <summary>Pusty układ — brak korzenia.</summary>
    public static LayoutTree Empty { get; } = new((LayoutNode?)null);

    /// <summary><see langword="true"/>, gdy układ nie ma żadnego liścia.</summary>
    public bool IsEmpty => Root is null;

    /// <summary>Liczba liści w układzie.</summary>
    public int LeafCount => Root?.LeafCount ?? 0;

    /// <summary>Zwraca nowy układ z podmienionym korzeniem.</summary>
    public LayoutTree WithRoot(LayoutNode? root) => new(root);
}
