using UberDashboard.Core.Localization;
using Microsoft.VisualBasic.FileIO;

namespace UberDashboard.Application.Import;

/// <summary>Parses CSV records using explicitly selected columns and delimiter.</summary>
public static class TodoCsvImport
{
    public static IEnumerable<ImportedTask> Parse(
        string source, char delimiter, bool hasHeader, int textColumn, int? statusColumn)
    {
        if (delimiter is not (',' or ';' or '\t'))
            throw new ArgumentOutOfRangeException(nameof(delimiter));
        ArgumentOutOfRangeException.ThrowIfNegative(textColumn);
        if (statusColumn is int index)
            ArgumentOutOfRangeException.ThrowIfNegative(index);

        using var parser = new TextFieldParser(new StringReader(source));
        parser.SetDelimiters(delimiter.ToString());
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;
        if (hasHeader && !parser.EndOfData)
            parser.ReadFields();

        while (!parser.EndOfData)
        {
            var line = checked((int)parser.LineNumber);
            var fields = parser.ReadFields() ?? [];
            if (textColumn >= fields.Length || statusColumn >= fields.Length)
                throw new FormatException(UiText.Format("TheSpecifiedColumnIsMissingInRow", line));

            var text = fields[textColumn].Trim();
            if (text.Length == 0)
                continue;

            var status = statusColumn is int column ? fields[column].Trim().ToLowerInvariant() : "";
            yield return new ImportedTask(text, status switch
            {
                "true" or "1" or "x" or "done" or "completed" or "tak" or "wykonane" => true,
                "" or "false" or "0" or "open" or "notstarted" or "nie" or "niewykonane" => false,
                _ => null,
            }, line);
        }
    }
}
