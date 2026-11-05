using System.Globalization;
using System.Text.Json;

namespace UberDashboard.Core.Localization;

/// <summary>Contains the application's Polish and English UI messages.</summary>
public static class UiText
{
    public const string Polish = "pl";
    public const string English = "en";

    private static readonly Lazy<Dictionary<string, string[]>> Catalog = new(LoadCatalog);

    public static string Language { get; private set; } = Polish;
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == English ? "en-US" : "pl-PL");

    public static string NormalizeLanguage(string? language) => language == English ? English : Polish;

    /// <summary>Sets the language before the application's windows and panel models are created.</summary>
    public static void Configure(string? language)
    {
        Language = NormalizeLanguage(language);
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }

    public static string Get(string key) => Get(key, Language);

    public static string Get(string key, string? language)
    {
        var translations = Catalog.Value[key];
        return translations[NormalizeLanguage(language) == English ? 1 : 0];
    }

    public static string Format(string key, params object?[] values) =>
        string.Format(Culture, Get(key), values);

    private static Dictionary<string, string[]> LoadCatalog()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("UberDashboard.Core.Localization.Messages.json")
            ?? throw new InvalidOperationException("The UI message catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidOperationException("The UI message catalog is empty.");
    }
}
