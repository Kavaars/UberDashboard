namespace UberDashboard.Contracts;

/// <summary>
/// Wersje kontraktu udostępnianego wtyczkom. Major oznacza zmianę łamiącą,
/// minor rozszerzenie zgodne wstecznie.
/// </summary>
public static class ContractVersions
{
    /// <summary>Wersja API kontraktu wtyczek, np. w manifeście jako <c>apiVersion</c>.</summary>
    public const string PanelApiVersion = "1.2.0";
}

/// <summary>Wersja semantyczna złożona z liczb major, minor i patch.</summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
{
    /// <summary>Parsuje tekst w formacie <c>major.minor.patch</c>; brakujące segmenty uznaje za zero.</summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
            {
                return false;
            }
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
