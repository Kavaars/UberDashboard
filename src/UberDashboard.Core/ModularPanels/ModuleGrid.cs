namespace UberDashboard.Core.ModularPanels;

/// <summary>A stable element's position in a modular panel's logical grid.</summary>
public sealed record ModulePosition(string Id, int Column, int Row);

/// <summary>Pure grid operations; positions are independent of screen pixels and panel size.</summary>
public static class ModuleGrid
{
    public const int MaximumCoordinate = 4095;

    /// <summary>Restore valid unique positions and append missing modules in the supplied default order.</summary>
    public static IReadOnlyList<ModulePosition> Restore(IEnumerable<string> availableIds, IEnumerable<ModulePosition>? saved, int columns)
    {
        ArgumentNullException.ThrowIfNull(availableIds);
        ValidateColumns(columns);
        var ids = availableIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        var allowed = ids.ToHashSet(StringComparer.Ordinal);
        var positions = new Dictionary<string, ModulePosition>(StringComparer.Ordinal);
        var used = new HashSet<int>();
        foreach (var item in saved ?? [])
        {
            if (item is null || !allowed.Contains(item.Id) || positions.ContainsKey(item.Id) || item.Column < 0 || item.Row < 0 ||
                item.Column >= columns || item.Row > MaximumCoordinate) continue;
            var slot = checked(item.Row * columns + item.Column);
            if (!used.Add(slot)) continue;
            positions.Add(item.Id, item);
        }
        var next = 0;
        foreach (var id in ids)
        {
            if (positions.ContainsKey(id)) continue;
            while (used.Contains(next)) next++;
            if (next / columns > MaximumCoordinate) throw new InvalidOperationException("Too many modules for the grid.");
            positions.Add(id, new(id, next % columns, next / columns));
            used.Add(next++);
        }
        return ids.Select(id => positions[id]).ToArray();
    }

    /// <summary>Move to a cell and shift occupied cells forward until the next empty cell.</summary>
    public static IReadOnlyList<ModulePosition> Move(IReadOnlyList<ModulePosition> positions, string id, int column, int row, int columns)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ValidateColumns(columns);
        if (column < 0 || column >= columns || row < 0 || row > MaximumCoordinate) throw new ArgumentOutOfRangeException(nameof(column));
        var ids = positions.Select(p => p.Id).ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || !ids.Contains(id, StringComparer.Ordinal))
            throw new ArgumentException("Module identities must be unique and include the moved item.", nameof(positions));
        var slots = new Dictionary<int, ModulePosition>();
        foreach (var item in positions)
        {
            if (item.Column < 0 || item.Column >= columns || item.Row < 0 || item.Row > MaximumCoordinate || !slots.TryAdd(item.Row * columns + item.Column, item))
                throw new ArgumentException("Grid positions must be valid and distinct.", nameof(positions));
        }
        var moving = positions.Single(p => p.Id == id);
        slots.Remove(moving.Row * columns + moving.Column);
        var target = row * columns + column;
        var empty = target;
        while (slots.ContainsKey(empty))
        {
            empty++;
            if (empty / columns > MaximumCoordinate) throw new InvalidOperationException("No room to move modules.");
        }
        for (var slot = empty; slot > target; slot--)
            if (slots.Remove(slot - 1, out var displaced)) slots[slot] = displaced with { Column = slot % columns, Row = slot / columns };
        slots[target] = moving with { Column = column, Row = row };
        var byId = slots.Values.ToDictionary(p => p.Id, StringComparer.Ordinal);
        return ids.Select(key => byId[key]).ToArray();
    }

    /// <summary>Maps logical slots to non-overlapping visual cells, wrapping wide modules as necessary.</summary>
    public static IReadOnlyList<(int Start, int Span)> Arrange(IReadOnlyList<(int Slot, int Span)> modules, int columns)
    {
        ValidateColumns(columns);
        var result = new (int Start, int Span)[modules.Count];
        var next = 0;
        var offset = 0;
        for (var i = 0; i < modules.Count; i++)
        {
            var (slot, requestedSpan) = modules[i];
            if (slot < 0 || requestedSpan < 1 || (i > 0 && slot <= modules[i - 1].Slot))
                throw new ArgumentException("Slots must be ordered, distinct and non-negative, with positive spans.", nameof(modules));
            var span = Math.Min(requestedSpan, columns);
            var start = Math.Max(next, slot + offset);
            if (start % columns + span > columns) start += columns - start % columns;
            result[i] = (start, span);
            next = start + span;
            offset = next - slot - 1;
        }
        return result;
    }

    private static void ValidateColumns(int columns)
    {
        if (columns is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(columns));
    }
}
