using UberDashboard.Core.Localization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using UberDashboard.Contracts;
using UberDashboard.Contracts.Plugins;

namespace UberDashboard.App.Plugins;

/// <summary>Walidacja i rozpakowanie bez uruchamiania kodu; wywoływać w tle.</summary>
public static partial class PluginPackage
{
    public const long MaxPackageBytes = 64 * 1024 * 1024;
    public const long MaxExpandedBytes = 128 * 1024 * 1024;
    public const int MaxFiles = 512;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static async Task<PluginManifest> ExtractAsync(string package, string destination, string hostVersion, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!string.Equals(Path.GetExtension(package), ".udplugin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("ChooseAUdpluginPackage"));
        await using var input = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        if (input.Length > MaxPackageBytes) throw new InvalidDataException(UiText.Get("ThePackageExceedsThe64MibLimit"));
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count is 0 or > MaxFiles) throw new InvalidDataException(UiText.Get("ThePackageMustContainBetween1And"));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            ValidatePath(entry.FullName);
            if (!names.Add(entry.FullName.Replace('\\', '/').TrimEnd('/')))
                throw new InvalidDataException(UiText.Get("ThePackageContainsDuplicatePaths"));
            // UNIX symlinks and Windows reparse points are never extracted.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException(UiText.Get("LinksAreNotAllowedInThePackage"));
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExpandedBytes) throw new InvalidDataException(UiText.Get("TheUnpackedPackageExceedsThe128Mib"));
        }
        var manifestEntry = zip.GetEntry("plugin.json") ?? throw new InvalidDataException(UiText.Get("PluginJsonIsMissingFromThePackage"));
        if (manifestEntry.Length > 65536) throw new InvalidDataException(UiText.Get("TheManifestIsTooLarge"));
        PluginManifest manifest;
        try
        {
            await using var stream = manifestEntry.Open();
            manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(stream, JsonOptions, token)
                ?? throw new InvalidDataException(UiText.Get("EmptyManifest"));
        }
        catch (JsonException ex) { throw new InvalidDataException(UiText.Get("InvalidJsonManifest"), ex); }
        ValidateManifest(manifest, hostVersion);
        if (!names.Contains(manifest.EntryAssembly)) throw new InvalidDataException(UiText.Get("ThePluginEntryAssemblyIsMissing"));
        Directory.CreateDirectory(destination);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(UiText.Get("ThePathPointsOutsideThePluginDirectory"));
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long count = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                count += read;
                if (count > entry.Length) throw new InvalidDataException(UiText.Get("ZipEntrySizeDoesNotMatch"));
                await output.WriteAsync(buffer.AsMemory(0, read), token);
            }
            if (count != entry.Length) throw new InvalidDataException(UiText.Get("IncompleteZipEntry"));
        }
        return manifest;
    }

    public static void ValidateManifest(PluginManifest value, string hostVersion)
    {
        if (value.Id is null || !PluginIdPattern().IsMatch(value.Id) || value.Id.StartsWith("pl.uberdashboard.builtin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("InvalidOrReservedPluginId"));
        if (string.IsNullOrWhiteSpace(value.Name) || value.Name.Length > 120 || !SemanticVersion.TryParse(value.Version, out _))
            throw new InvalidDataException(UiText.Get("AValidNameOrVersionIsMissing"));
        var compatibility = PluginApiCompatibility.Evaluate(value, hostVersion, ContractVersions.PanelApiVersion);
        if (!compatibility.IsCompatible) throw new InvalidDataException(UiText.Format("ThePluginIsIncompatibleWithThisApp", compatibility.Issue));
        if (value.TargetFramework != "net10.0-windows") throw new InvalidDataException(UiText.Get("ThePluginRequiresADifferentPlatformThe"));
        if (string.IsNullOrWhiteSpace(value.EntryAssembly) || Path.GetFileName(value.EntryAssembly) != value.EntryAssembly || !value.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("EntryassemblyMustBeADllFileName"));
        ValidatePath(value.EntryAssembly);
        if (string.IsNullOrWhiteSpace(value.EntryType) || value.DataSchemaVersion < 1 || value.Panels is null || value.Panels.Count is 0 or > 32
            || value.Panels.Any(p => p is null || string.IsNullOrWhiteSpace(p.TypeId) || p.TypeId.Length > 160 || string.IsNullOrWhiteSpace(p.Name))
            || value.Panels.Select(p => p.TypeId).Distinct(StringComparer.Ordinal).Count() != value.Panels.Count)
            throw new InvalidDataException(UiText.Get("InvalidPanelOrEntryTypeDeclaration"));
        if (value.Dependencies is null || value.Capabilities is null) throw new InvalidDataException(UiText.Get("InvalidDependencyOrFeatureList"));
    }

    private static void ValidatePath(string name)
    {
        var normalized = name.Replace('\\', '/');
        if (normalized.Length is 0 or > 240 || normalized.StartsWith('/') || normalized.Contains(':')) throw new InvalidDataException(UiText.Get("DisallowedPathInThePackage"));
        foreach (var part in normalized.TrimEnd('/').Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part is "" or "." or ".." || string.Equals(part, ".enabled", StringComparison.OrdinalIgnoreCase) || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedNamePattern().IsMatch(stem))
                throw new InvalidDataException(UiText.Get("DisallowedFileNameInThePackage"));
        }
    }
    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:[.-][a-z0-9]+){1,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginIdPattern();
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedNamePattern();
}
