namespace UberDashboard.Core.Layout;

/// <summary>
/// Wskazówka przywrócenia ukrytego panelu: preferowany sąsiad, oś, strona oraz
/// uporządkowana lista kandydatów zapasowych (najbliższy sąsiad pierwszy).
/// </summary>
public sealed record RestoreHint(
    PanelInstanceId? Sibling,
    SplitAxis Axis,
    bool WasFirstChild,
    double Ratio,
    IReadOnlyList<PanelInstanceId> FallbackCandidates)
{
    /// <summary>Wskazówka pusta — bez preferowanej pozycji.</summary>
    public static RestoreHint None { get; } = new(null, SplitAxis.Horizontal, true, 0.5d, []);

    /// <summary>Buduje wskazówkę na podstawie układu sprzed ukrycia panelu.</summary>
    public static RestoreHint For(LayoutTree treeBeforeRemoval, PanelInstanceId instanceId)
    {
        var location = LayoutWalker.Locate(treeBeforeRemoval.Root, instanceId);
        if (location is null)
        {
            return None;
        }

        var sibling = location.SiblingCandidates.Count > 0 ? location.SiblingCandidates[0] : (PanelInstanceId?)null;
        var fallbacks = location.SiblingCandidates.Count > 1
            ? location.SiblingCandidates.Skip(1).ToArray()
            : [];

        return new RestoreHint(sibling, location.Axis, location.IsFirstChild, location.Ratio, fallbacks);
    }
}
