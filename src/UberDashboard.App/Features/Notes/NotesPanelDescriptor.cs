using UberDashboard.Core.Localization;
using UberDashboard.App.Panels;
using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Notes;

/// <summary>
/// Opis typu panelu notatnika. Typ jest niezmienny w czasie działania i nie
/// przechowuje stanu instancji — stan żyje wyłącznie w rekordzie instancji.
/// </summary>
public sealed class NotesPanelDescriptor : IPanelDescriptor
{
    /// <summary>Identyfikator typu panelu notatnika.</summary>
    public const string TypeId = "notes";

    /// <summary>Tworzy opis typu panelu z fabryką instancji.</summary>
    public NotesPanelDescriptor(IPanelFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
    }

    string IPanelDescriptor.TypeId => TypeId;

    /// <inheritdoc />
    public string DisplayName => UiText.Get("Notepad");

    /// <inheritdoc />
    public string? IconKey => "panel.notes";

    /// <inheritdoc />
    public MinSize MinSize => new(280d, 150d);

    /// <inheritdoc />
    public int StateSchemaVersion => NotesDocument.SchemaVersion;

    /// <inheritdoc />
    public IPanelFactory Factory { get; }
}
