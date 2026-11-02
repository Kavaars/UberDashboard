namespace UberDashboard.Core.Layout.Serialization;

/// <summary>Wynik odczytu dokumentu układu.</summary>
public sealed record LayoutDeserializeResult
{
    /// <summary>Odczytany układ lub <see langword="null"/> przy błędzie.</summary>
    public LayoutTree? Tree { get; init; }

    /// <summary>Kod błędu; <see cref="LayoutErrorCode.None"/> przy powodzeniu.</summary>
    public required LayoutErrorCode Error { get; init; }

    /// <summary>Wersja schematu odczytana z dokumentu.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Opis błędu dla logu i komunikatu UI.</summary>
    public string? Message { get; init; }

    /// <summary><see langword="true"/>, gdy odczyt się powiódł.</summary>
    public bool IsSuccess => Error == LayoutErrorCode.None && Tree is not null;

    /// <summary>
    /// <see langword="true"/>, gdy aplikacja ma zachować oryginalny dokument i nie
    /// nadpisywać go pustym lub zmigrowanym stanem.
    /// </summary>
    public bool MustPreserveOriginalDocument =>
        Error is LayoutErrorCode.UnknownSchemaVersion or LayoutErrorCode.MalformedDocument;

    /// <summary>Wynik udany.</summary>
    public static LayoutDeserializeResult Ok(LayoutTree tree, int schemaVersion) =>
        new() { Tree = tree, Error = LayoutErrorCode.None, SchemaVersion = schemaVersion };

    /// <summary>Wynik nieudany.</summary>
    public static LayoutDeserializeResult Fail(LayoutErrorCode error, int schemaVersion, string message) =>
        new() { Tree = null, Error = error, SchemaVersion = schemaVersion, Message = message };
}
