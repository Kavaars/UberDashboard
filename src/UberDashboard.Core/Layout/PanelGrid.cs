namespace UberDashboard.Core.Layout;

/// <summary>Prostokąt panelu w komórkach siatki pulpitu.</summary>
public readonly record struct GridCell(int Column, int Row, int ColumnSpan, int RowSpan)
{
    public bool Overlaps(GridCell other) =>
        Column < other.Column + other.ColumnSpan && other.Column < Column + ColumnSpan &&
        Row < other.Row + other.RowSpan && other.Row < Row + RowSpan;

    /// <summary>Przycina rozmiar, a potem pozycję, tak by prostokąt mieścił się w siatce.</summary>
    public GridCell Clamp(int columns, int rows)
    {
        var width = Math.Clamp(ColumnSpan, 1, columns);
        var height = Math.Clamp(RowSpan, 1, rows);
        return new(Math.Clamp(Column, 0, columns - width), Math.Clamp(Row, 0, rows - height), width, height);
    }
}

public enum GridEdge { Left, Top, Right, Bottom }

/// <summary>Czyste operacje swobodnej siatki paneli; panele przyciągają się do komórek, nie do siebie.</summary>
public static class PanelGrid
{
    /// <summary>Przesuwa wskazaną krawędź, zachowując przeciwną i co najmniej jedną komórkę.</summary>
    public static GridCell ResizeEdge(GridCell cell, GridEdge edge, int delta) => edge switch
    {
        GridEdge.Left => cell with { Column = cell.Column + Math.Min(delta, cell.ColumnSpan - 1), ColumnSpan = cell.ColumnSpan - Math.Min(delta, cell.ColumnSpan - 1) },
        GridEdge.Top => cell with { Row = cell.Row + Math.Min(delta, cell.RowSpan - 1), RowSpan = cell.RowSpan - Math.Min(delta, cell.RowSpan - 1) },
        GridEdge.Right => cell with { ColumnSpan = Math.Max(1, cell.ColumnSpan + delta) },
        GridEdge.Bottom => cell with { RowSpan = Math.Max(1, cell.RowSpan + delta) },
        _ => cell,
    };

    public static bool Fits(IReadOnlyDictionary<PanelInstanceId, GridCell> placed, PanelInstanceId self, GridCell cell, int columns, int rows) =>
        cell.Column >= 0 && cell.Row >= 0 && cell.ColumnSpan >= 1 && cell.RowSpan >= 1 &&
        cell.Column + cell.ColumnSpan <= columns && cell.Row + cell.RowSpan <= rows &&
        !placed.Any(pair => pair.Key != self && pair.Value.Overlaps(cell));

    /// <summary>Pierwsze wolne miejsce (wierszami) dla żądanego rozmiaru, a gdy go brak — dla coraz mniejszego.</summary>
    public static GridCell? FindFree(IReadOnlyDictionary<PanelInstanceId, GridCell> placed, int width, int height, int columns, int rows)
    {
        for (var (w, h) = (Math.Clamp(width, 1, columns), Math.Clamp(height, 1, rows)); ; (w, h) = (Math.Max(1, w - 1), Math.Max(1, h - 1)))
        {
            for (var row = 0; row + h <= rows; row++)
                for (var column = 0; column + w <= columns; column++)
                    if (Fits(placed, default, new(column, row, w, h), columns, rows)) return new(column, row, w, h);
            if (w == 1 && h == 1) return null;
        }
    }

    /// <summary>Przelicza drzewo podziałów na komórki, zachowując proporcje dawnego układu.</summary>
    public static Dictionary<PanelInstanceId, GridCell> FromTree(LayoutNode? root, int columns, int rows)
    {
        var result = new Dictionary<PanelInstanceId, GridCell>();
        Visit(root, 0, 0, columns, rows);
        return result;

        void Visit(LayoutNode? node, int column, int row, int width, int height)
        {
            switch (node)
            {
                case LeafNode leaf:
                    result[leaf.InstanceId] = new GridCell(column, row, width, height).Clamp(columns, rows);
                    break;
                case SplitNode { Axis: SplitAxis.Horizontal } split:
                    var left = Math.Clamp((int)Math.Round(width * split.Ratio), 1, Math.Max(1, width - 1));
                    Visit(split.First, column, row, left, height);
                    Visit(split.Second, column + left, row, Math.Max(1, width - left), height);
                    break;
                case SplitNode split:
                    var top = Math.Clamp((int)Math.Round(height * split.Ratio), 1, Math.Max(1, height - 1));
                    Visit(split.First, column, row, width, top);
                    Visit(split.Second, column, row + top, width, Math.Max(1, height - top));
                    break;
            }
        }
    }

    /// <summary>Skaluje prostokąt po zmianie gęstości siatki.</summary>
    public static GridCell Rescale(GridCell cell, int fromColumns, int fromRows, int columns, int rows)
    {
        if (fromColumns < 1 || fromRows < 1 || (fromColumns == columns && fromRows == rows)) return cell.Clamp(columns, rows);
        int Scale(int value, int from, int to) => (int)Math.Round(value * (double)to / from);
        return new GridCell(Scale(cell.Column, fromColumns, columns), Scale(cell.Row, fromRows, rows),
            Math.Max(1, Scale(cell.ColumnSpan, fromColumns, columns)), Math.Max(1, Scale(cell.RowSpan, fromRows, rows))).Clamp(columns, rows);
    }
}
