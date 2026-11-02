using UberDashboard.Core.Layout;

namespace UberDashboard.Contracts.Panels;

/// <summary>
/// Opis typu panelu rejestrowany przez wtyczkę lub moduł wbudowany. Opis jest
/// niezmienny w czasie działania i nie przechowuje stanu instancji.
/// </summary>
public interface IPanelDescriptor
{
    /// <summary>Unikalny w obrębie wtyczki identyfikator typu, np. <c>todo</c>.</summary>
    string TypeId { get; }

    /// <summary>Nazwa prezentowana użytkownikowi.</summary>
    string DisplayName { get; }

    /// <summary>Klucz ikony używany przez warstwę widoku; może być pusty.</summary>
    string? IconKey { get; }

    /// <summary>Minimalny rozmiar instancji tego typu w DIP.</summary>
    MinSize MinSize { get; }

    /// <summary>Wersja schematu stanu zwracanego przez instancję.</summary>
    int StateSchemaVersion { get; }

    /// <summary>Fabryka tworząca instancje panelu.</summary>
    IPanelFactory Factory { get; }
}

/// <summary>Fabryka instancji panelu dla przekazanego kontekstu.</summary>
public interface IPanelFactory
{
    /// <summary>
    /// Tworzy instancję panelu. Identyfikator instancji pochodzi z kontekstu:
    /// host nadaje nowy identyfikator wyłącznie przy dodawaniu instancji.
    /// </summary>
    ValueTask<IPanelInstance> CreateAsync(PanelContext context, CancellationToken cancellationToken);
}
