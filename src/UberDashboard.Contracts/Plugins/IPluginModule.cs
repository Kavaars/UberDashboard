using UberDashboard.Contracts.Panels;

namespace UberDashboard.Contracts.Plugins;

/// <summary>
/// Wejście wtyczki. Host tworzy moduł z typu wskazanego w manifeście i wywołuje
/// <see cref="InitializeAsync"/> na wątku poza Dispatcherem.
/// </summary>
public interface IPluginModule
{
    /// <summary>Rejestruje metadane paneli bez blokowania interfejsu.</summary>
    ValueTask InitializeAsync(IPluginHostContext hostContext, CancellationToken cancellationToken);
}

/// <summary>Kontrakty, których wtyczka używa do komunikacji z hostem.</summary>
public interface IPluginHostContext
{
    /// <summary>Wersja hosta w formacie semantycznym.</summary>
    string HostVersion { get; }

    /// <summary>Wersja kontraktu API hosta w formacie semantycznym.</summary>
    string ContractVersion { get; }

    /// <summary>Kanał logowania wtyczki (wspólny dla wszystkich jej instancji).</summary>
    Contracts.Host.IPanelLogger Logger { get; }

    /// <summary>Rejestruje opis typu panelu. Ponowna rejestracja tego samego <c>TypeId</c> jest błędem.</summary>
    void RegisterPanel(IPanelDescriptor descriptor);
}
