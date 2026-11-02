namespace UberDashboard.Core.Layout;

/// <summary>
/// Stabilny identyfikator instancji panelu. Nadawany przez host przy dodaniu instancji
/// i zachowywany przez cały cykl życia, również po ukryciu i przywróceniu panelu.
/// </summary>
public readonly record struct PanelInstanceId(Guid Value)
{
    /// <summary>Pusty identyfikator — wartość niepoprawna w układzie.</summary>
    public static PanelInstanceId Empty { get; } = new(Guid.Empty);

    /// <summary>Tworzy nowy, losowy identyfikator instancji.</summary>
    public static PanelInstanceId New() => new(Guid.NewGuid());

    /// <summary><see langword="true"/>, gdy identyfikator nie został ustawiony.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("D");
}
