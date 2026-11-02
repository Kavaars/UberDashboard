using UberDashboard.Core.Layout;

namespace UberDashboard.Application.Abstractions;

/// <summary>Wynik wczytania dokumentu układu z magazynu.</summary>
public sealed record LayoutLoadResult(
    LayoutTree Tree,
    int SchemaVersion,
    LayoutErrorCode Error,
    string? Message,
    bool MustPreserveOriginalDocument)
{
    /// <summary><see langword="true"/>, gdy dokument został odczytany poprawnie.</summary>
    public bool IsSuccess => Error == LayoutErrorCode.None;

    /// <summary>Wynik pusty dla pulpitu bez zapisanego układu.</summary>
    public static LayoutLoadResult Empty(int schemaVersion) =>
        new(LayoutTree.Empty, schemaVersion, LayoutErrorCode.None, null, false);

    /// <summary>Wynik nieudany z zachowaniem oryginalnego dokumentu.</summary>
    public static LayoutLoadResult Fail(LayoutErrorCode error, int schemaVersion, string message, bool preserve) =>
        new(LayoutTree.Empty, schemaVersion, error, message, preserve);
}

/// <summary>Magazyn wersjonowanych dokumentów układu, kluczowany po identyfikatorze pulpitu.</summary>
public interface ILayoutStore
{
    /// <summary>Wczytuje układ pulpitu; dla brakującego wiersza zwraca pusty układ.</summary>
    Task<LayoutLoadResult> LoadAsync(Guid dashboardId, CancellationToken cancellationToken = default);

    /// <summary>Zapisuje układ pulpitu w jednej transakcji.</summary>
    Task SaveAsync(Guid dashboardId, LayoutTree tree, CancellationToken cancellationToken = default);
}
