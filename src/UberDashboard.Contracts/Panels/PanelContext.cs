using UberDashboard.Core.Layout;
using UberDashboard.Contracts.Host;

namespace UberDashboard.Contracts.Panels;

/// <summary>
/// Kontekst przekazywany panelowi przy tworzeniu. Udostępnia wyłącznie kontrakty
/// hosta — bez surowego połączenia do bazy.
/// </summary>
public sealed record PanelContext
{
    /// <summary>Stabilny identyfikator instancji, po którym kluczowane są dane.</summary>
    public required PanelInstanceId InstanceId { get; init; }

    /// <summary>Identyfikator wtyczki w formacie reverse-DNS.</summary>
    public required string PluginId { get; init; }

    /// <summary>Identyfikator pulpitu, do którego należy instancja.</summary>
    public required string DashboardId { get; init; }

    /// <summary>Token anulowany przy zamknięciu, ukryciu lub usunięciu instancji.</summary>
    public required CancellationToken CloseToken { get; init; }

    /// <summary>Kanał logowania opatrzony identyfikatorem instancji.</summary>
    public required IPanelLogger Logger { get; init; }

    /// <summary>Kanał nieblokujących powiadomień.</summary>
    public required IPanelNotifier Notifier { get; init; }

    /// <summary>Namespacowany magazyn plików instancji.</summary>
    public required IPanelStorage Storage { get; init; }
}
