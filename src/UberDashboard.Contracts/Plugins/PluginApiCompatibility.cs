namespace UberDashboard.Contracts.Plugins;

/// <summary>Powód niezgodności wtyczki z hostem.</summary>
public enum PluginCompatibilityIssue
{
    /// <summary>Wtyczka jest zgodna.</summary>
    None,
    /// <summary>Brak lub niepoprawna wersja API w manifeście.</summary>
    InvalidApiVersion,
    /// <summary>Różny numer major API — zmiana łamiąca kontrakt.</summary>
    ApiMajorMismatch,
    /// <summary>Minor API wtyczki jest wyższy niż minor hosta.</summary>
    ApiMinorTooNew,
    /// <summary>Host jest starszy niż wymagane <c>minHostVersion</c>.</summary>
    HostTooOld,
    /// <summary>Brak lub niepoprawna wersja hosta wymagana przez wtyczkę.</summary>
    InvalidMinHostVersion,
}

/// <summary>Wynik sprawdzenia zgodności wtyczki z hostem.</summary>
public readonly record struct PluginCompatibilityResult(bool IsCompatible, PluginCompatibilityIssue Issue)
{
    /// <summary>Wynik zgodny.</summary>
    public static PluginCompatibilityResult Compatible { get; } = new(true, PluginCompatibilityIssue.None);

    /// <summary>Wynik niezgodny z podanym powodem.</summary>
    public static PluginCompatibilityResult Incompatible(PluginCompatibilityIssue issue) => new(false, issue);
}

/// <summary>
/// Sprawdza zgodność wtyczki z hostem: zgodny major API, minor nie większy niż
/// hosta oraz host nie starszy niż wymagane minimum.
/// </summary>
public static class PluginApiCompatibility
{
    /// <summary>Ocenia zgodność manifestu z wersją hosta i wersją kontraktu.</summary>
    public static PluginCompatibilityResult Evaluate(
        PluginManifest manifest,
        string hostVersion,
        string hostContractVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!SemanticVersion.TryParse(manifest.ApiVersion, out var api))
        {
            return PluginCompatibilityResult.Incompatible(PluginCompatibilityIssue.InvalidApiVersion);
        }

        if (!SemanticVersion.TryParse(hostContractVersion, out var contract))
        {
            return PluginCompatibilityResult.Incompatible(PluginCompatibilityIssue.InvalidApiVersion);
        }

        if (api.Major != contract.Major)
        {
            return PluginCompatibilityResult.Incompatible(PluginCompatibilityIssue.ApiMajorMismatch);
        }

        if (api.Minor > contract.Minor)
        {
            return PluginCompatibilityResult.Incompatible(PluginCompatibilityIssue.ApiMinorTooNew);
        }

        if (!SemanticVersion.TryParse(manifest.MinHostVersion, out var minHost))
        {
            return PluginCompatibilityResult.Incompatible(PluginCompatibilityIssue.InvalidMinHostVersion);
        }

        if (!SemanticVersion.TryParse(hostVersion, out var host))
        {
            return PluginCompatibilityResult.Incompatible(PluginCompatibilityIssue.InvalidMinHostVersion);
        }

        return host.CompareTo(minHost) < 0
            ? PluginCompatibilityResult.Incompatible(PluginCompatibilityIssue.HostTooOld)
            : PluginCompatibilityResult.Compatible;
    }
}
