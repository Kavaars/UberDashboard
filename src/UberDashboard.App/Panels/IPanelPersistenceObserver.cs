namespace UberDashboard.App.Panels;

/// <summary>
/// Opcjonalny kanał informowania instancji panelu o udanym zapisie jej stanu.
/// Pozwala panelowi pokazać rzetelny wskaźnik „niezapisane" zamiast zgadywania,
/// kiedy dane faktycznie trafiły do magazynu.
/// </summary>
public interface IPanelPersistenceObserver
{
    /// <summary>Wywoływane po udanym zapisie stanu instancji.</summary>
    void OnStatePersisted();

    long ChangeVersion => 0;
}
