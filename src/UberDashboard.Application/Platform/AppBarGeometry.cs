namespace UberDashboard.Application.Platform;

/// <summary>
/// Czyste przeliczenia geometrii paska. Nie zawierają wywołań systemowych, dzięki
/// czemu kolejność „query → refit → set” można sprawdzić w testach jednostkowych.
/// </summary>
public static class AppBarGeometry
{
    /// <summary>
    /// Prostokąt, o jaki aplikacja prosi powłokę dla wybranej krawędzi i grubości paska.
    /// Grubość jest zaciskana do nieujemnej wartości mieszczącej się w obszarze roboczym.
    /// </summary>
    public static PixelRect RequestedRect(PixelRect workArea, AppBarEdge edge, int thickness)
    {
        var clamped = ClampThickness(workArea, edge, thickness);

        return edge switch
        {
            AppBarEdge.Left => new PixelRect(workArea.Left, workArea.Top, workArea.Left + clamped, workArea.Bottom),
            AppBarEdge.Right => new PixelRect(workArea.Right - clamped, workArea.Top, workArea.Right, workArea.Bottom),
            AppBarEdge.Top => new PixelRect(workArea.Left, workArea.Top, workArea.Right, workArea.Top + clamped),
            _ => new PixelRect(workArea.Left, workArea.Bottom - clamped, workArea.Right, workArea.Bottom),
        };
    }

    /// <summary>
    /// Ponownie dopasowuje żądaną grubość do prostokąta zwróconego przez <c>ABM_QUERYPOS</c>.
    /// Powłoka może przesunąć lub skrócić prostokąt, dlatego pozycję bierzemy z odpowiedzi,
    /// a grubość przywracamy po stronie właściwej krawędzi.
    /// </summary>
    public static PixelRect RefitThickness(PixelRect queried, AppBarEdge edge, int thickness)
    {
        var clamped = Math.Max(0, thickness);

        return edge switch
        {
            AppBarEdge.Left => queried.WithWidth(clamped),
            AppBarEdge.Right => queried with { Left = queried.Right - clamped },
            AppBarEdge.Top => queried.WithHeight(clamped),
            _ => queried with { Top = queried.Bottom - clamped },
        };
    }

    /// <summary>Prostokąt zwrócony przez powłokę nadaje się do zajęcia obszaru.</summary>
    public static bool IsUsable(PixelRect rect) => rect.IsValid;

    private static int ClampThickness(PixelRect workArea, AppBarEdge edge, int thickness)
    {
        if (thickness <= 0)
        {
            return 0;
        }

        var limit = edge is AppBarEdge.Left or AppBarEdge.Right
            ? Math.Max(0, workArea.Width)
            : Math.Max(0, workArea.Height);

        return Math.Min(thickness, limit);
    }
}
