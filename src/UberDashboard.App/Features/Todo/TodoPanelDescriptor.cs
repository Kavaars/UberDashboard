using UberDashboard.Contracts.Panels;
using UberDashboard.Core.Layout;

namespace UberDashboard.App.Features.Todo;

/// <summary>
/// Opis typu panelu listy zadań. Typ jest niezmienny w czasie działania i nie
/// przechowuje stanu instancji — stan żyje wyłącznie w rekordzie instancji.
/// </summary>
public sealed class TodoPanelDescriptor : IPanelDescriptor
{
    /// <summary>Identyfikator typu panelu listy zadań.</summary>
    public const string TypeId = "todo";

    /// <summary>Tworzy opis typu panelu z fabryką instancji.</summary>
    public TodoPanelDescriptor(IPanelFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Factory = factory;
    }

    string IPanelDescriptor.TypeId => TypeId;

    /// <inheritdoc />
    public string DisplayName => "Lista zadań";

    /// <inheritdoc />
    public string? IconKey => "panel.todo";

    /// <inheritdoc />
    public MinSize MinSize => new(280d, 220d);

    /// <inheritdoc />
    public int StateSchemaVersion => TodoDocument.SchemaVersion;

    /// <inheritdoc />
    public IPanelFactory Factory { get; }
}
