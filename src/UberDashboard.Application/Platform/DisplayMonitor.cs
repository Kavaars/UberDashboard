namespace UberDashboard.Application.Platform;

/// <summary>
/// Monitor widziany przez hosta. Geometria jest w pikselach właściwych dla kontekstu
/// DPI tego monitora; konwersję do DIP wykonuje <see cref="DpiConverter"/>.
/// </summary>
/// <param name="Id">Stabilny identyfikator urządzenia monitora (np. <c>\\.\DISPLAY1</c>).</param>
/// <param name="Bounds">Pełny obszar monitora w pikselach (<c>rcMonitor</c>).</param>
/// <param name="WorkArea">Obszar roboczy z wyłączeniem pasków systemowych (<c>rcWork</c>).</param>
/// <param name="Dpi">Efektywne DPI; 96 oznacza skalę 100%.</param>
/// <param name="IsPrimary">Monitor jest monitorem głównym powłoki.</param>
public sealed record DisplayMonitor(
    string Id,
    PixelRect Bounds,
    PixelRect WorkArea,
    uint Dpi,
    bool IsPrimary)
{
    /// <summary>Monitor ma identyfikator i niepustą geometrię.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(Id) && Bounds.IsValid;
}
