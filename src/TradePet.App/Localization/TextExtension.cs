using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using TradePet.Core.Localization;

namespace TradePet.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Key);
}

[MarkupExtensionReturnType(typeof(XmlLanguage))]
public sealed class LanguageExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => XmlLanguage.GetLanguage(UiText.Language);
}

public sealed class UiTranslationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text ? UiText.Translate(text) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value;
}
