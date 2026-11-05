using System.Globalization;

namespace PhotoFrame.Plugin;

internal static class PhotoText
{
    public static string Get(string polish, string english) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? english : polish;
}
