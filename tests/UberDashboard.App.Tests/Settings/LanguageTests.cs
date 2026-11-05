using UberDashboard.App.Settings;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Tests.Settings;

public sealed class LanguageTests
{
    [Fact]
    public void Missing_or_unknown_language_defaults_to_polish()
    {
        Assert.Equal("pl", ApplicationSettings.Default.Language);
        Assert.Equal("pl", new ApplicationSettings { Language = "invalid" }.Normalize().Language);
        Assert.Equal("en", new ApplicationSettings { Language = "en" }.Normalize().Language);
    }

    [Theory]
    [InlineData("pl", "Ustawienia", "Aktywny tryb: Tryb")]
    [InlineData("en", "Settings", "Active mode: Tryb")]
    public void Catalog_formats_each_language_without_translating_user_values(string language, string label, string formatted)
    {
        Assert.Equal(label, UiText.Get("Settings", language));
        Assert.Equal(formatted, string.Format(UiText.Get("ActiveMode", language), "Tryb"));
    }
}
