namespace UberDashboard.Application.Import;

public sealed record ImportedTask(string Text, bool? IsDone, int Line, bool IsNested = false);

/// <summary>Reads task candidates without modifying the source or the destination list.</summary>
public static class TodoTextImport
{
    public static IEnumerable<ImportedTask> Parse(string source, bool markdown)
    {
        using var reader = new StringReader(source);
        char fence = '\0';
        var fenceLength = 0;
        var lineNumber = 0;
        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            if (markdown && line[0] is '`' or '~')
            {
                var marker = line[0];
                var length = 0;
                while (length < line.Length && line[length] == marker)
                    length++;

                if (fence == '\0' && length >= 3)
                {
                    fence = marker;
                    fenceLength = length;
                    continue;
                }
                if (fence == marker && length >= fenceLength && line.Length == length)
                {
                    fence = '\0';
                    continue;
                }
            }
            if (fence != '\0')
                continue;

            var markerLength = ListMarkerLength(line);
            if (markdown && markerLength == 0)
                continue;
            var candidate = line[markerLength..].TrimStart();
            if (candidate.Length >= 4 && candidate[0] == '[' && candidate[2] == ']' &&
                char.IsWhiteSpace(candidate[3]))
            {
                var content = candidate[4..].Trim();
                if (content.Length > 0)
                    yield return new ImportedTask(content, candidate[1] switch
                    {
                        ' ' => false,
                        'x' or 'X' => true,
                        _ => null,
                    }, lineNumber, markdown && char.IsWhiteSpace(raw[0]));
            }
            else if (!markdown)
            {
                yield return new ImportedTask(line, false, lineNumber);
            }
        }
    }

    private static int ListMarkerLength(string line)
    {
        if (line.Length >= 2 && line[0] is '-' or '+' or '*' && char.IsWhiteSpace(line[1]))
            return 2;
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
            digits++;
        return digits > 0 && digits + 1 < line.Length && line[digits] is '.' or ')' &&
            char.IsWhiteSpace(line[digits + 1]) ? digits + 2 : 0;
    }
}
