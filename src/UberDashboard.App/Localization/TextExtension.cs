using System.Windows.Markup;
using UberDashboard.Core.Localization;

namespace UberDashboard.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(key);
}

[MarkupExtensionReturnType(typeof(XmlLanguage))]
public sealed class LanguageExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => XmlLanguage.GetLanguage(UiText.Culture.Name);
}
