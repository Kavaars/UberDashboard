namespace UberDashboard.Core.Layout;

/// <summary>
/// Wynik operacji na układzie. Operacja nieudana zwraca układ wejściowy bez zmian,
/// dzięki czemu edytor nigdy nie zapisuje stanu częściowo zmodyfikowanego.
/// </summary>
public sealed record LayoutOperationResult
{
    /// <summary>Układ po operacji lub układ wejściowy, gdy operacja się nie powiodła.</summary>
    public required LayoutTree Tree { get; init; }

    /// <summary><see langword="true"/>, gdy operacja zakończyła się powodzeniem.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Kod błędu; <see cref="LayoutErrorCode.None"/> przy powodzeniu.</summary>
    public LayoutErrorCode Error { get; init; }

    /// <summary>Opis błędu dla logu i komunikatu UI.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// <see langword="true"/>, gdy przywracanie panelu użyło pozycji zapasowej,
    /// a nie zapamiętanej. UI wskazuje wtedy użytkownikowi miejsce odtworzenia.
    /// </summary>
    public bool UsedFallback { get; init; }

    /// <summary>Wynik udany.</summary>
    public static LayoutOperationResult Ok(LayoutTree tree, bool usedFallback = false) =>
        new() { Tree = tree, IsSuccess = true, Error = LayoutErrorCode.None, UsedFallback = usedFallback };

    /// <summary>Wynik nieudany; zwracany układ jest niezmieniony.</summary>
    public static LayoutOperationResult Fail(LayoutTree unchangedTree, LayoutErrorCode error, string message) =>
        new() { Tree = unchangedTree, IsSuccess = false, Error = error, ErrorMessage = message };
}
