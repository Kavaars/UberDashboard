using System.Windows;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Shell;

/// <summary>
/// Model widoku podziału układu. Przechowuje niezmienne proporcje użyte do
/// początkowej budowy siatki; dalsza zmiana proporcji przechodzi przez operację
/// domenową, a nie przez modyfikację tego obiektu.
/// </summary>
public sealed class PanelSplitViewModel : LayoutNodeViewModel
{
    /// <summary>Tworzy podział z dwojgiem dzieci.</summary>
    public PanelSplitViewModel(Guid splitId, SplitAxis axis, double ratio, LayoutNodeViewModel first, LayoutNodeViewModel second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        SplitId = splitId;
        Axis = axis;
        Ratio = double.IsFinite(ratio) ? ratio : 0.5d;
        First = first;
        Second = second;
    }

    /// <summary>Identyfikator podziału w dokumencie układu.</summary>
    public Guid SplitId { get; }

    /// <summary>Oś podziału.</summary>
    public SplitAxis Axis { get; }

    /// <summary>Proporcja udziału pierwszego dziecka.</summary>
    public double Ratio { get; }

    /// <summary>Pierwsze dziecko (lewo albo góra).</summary>
    public LayoutNodeViewModel First { get; }

    /// <summary>Drugie dziecko (prawo albo dół).</summary>
    public LayoutNodeViewModel Second { get; }

    /// <summary>Szerokość/wysokość pierwszego dziecka jako udział gwiazdkowy.</summary>
    public GridLength FirstLength => new(Clamp(Ratio), GridUnitType.Star);

    /// <summary>Szerokość/wysokość drugiego dziecka jako udział gwiazdkowy.</summary>
    public GridLength SecondLength => new(Clamp(1d - Ratio), GridUnitType.Star);

    private static double Clamp(double value) => value > 0.0001d ? value : 0.0001d;
}
