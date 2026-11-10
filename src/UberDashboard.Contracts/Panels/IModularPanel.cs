using UberDashboard.Core.ModularPanels;

namespace UberDashboard.Contracts.Panels;

/// <summary>
/// Optional per-instance modular layout. The host exposes its edit-mode command;
/// the panel supplies its own view, persistence and accessible movement controls.
/// Members and change notifications are used on the UI thread.
/// </summary>
public interface IModularPanel
{
    /// <summary>Transient editing state; do not persist this value.</summary>
    bool IsModuleEditMode { get; set; }
    /// <summary>Logical positions; the reference grid uses four columns (0–3) and zero-based rows.</summary>
    IReadOnlyList<ModulePosition> ModulePositions { get; }
    /// <summary>Returns the stable identity of the displayed item, or null when it is not a module.</summary>
    string? GetModuleId(object item);
    /// <summary>Moves a module when editing is enabled; the implementation persists and notifies the change.</summary>
    void MoveModuleToCell(string id, int column, int row);
}
