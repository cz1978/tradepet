using TradePet.Core.Localization;
using Xunit;

namespace TradePet.Core.Tests;

public sealed class UiTextTests
{
    [Fact]
    public void Catalog_AllEntriesHaveEnglishAndPreserveFormattingSlots()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("TradePet.Core.Localization.Strings.en.json")!;
        using var json = System.Text.Json.JsonDocument.Parse(stream);
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            var source = entry.GetProperty("source").GetString()!;
            var english = entry.GetProperty("english").GetString()!;
            Assert.NotEmpty(english);
            Assert.False(english.Any(c => c is >= '\u3400' and <= '\u9fff'), source);
            const string slots = @"\{\d+(?::[^{}]*)?\}";
            Assert.Equal(System.Text.RegularExpressions.Regex.Matches(source, slots).Select(m => m.Value).Order(),
                System.Text.RegularExpressions.Regex.Matches(english, slots).Select(m => m.Value).Order());
        }
    }

    [Fact]
    public void Translation_PreservesNumbersAndUntouchedUserText()
    {
        Assert.Equal("Save settings", UiText.Translate("保存设置", "en-US"));
        Assert.Equal("保存设置", UiText.Translate("保存设置", "zh-CN"));
        Assert.Equal("- Net P/L: -33.82", UiText.Translate("- 净盈亏：-33.82", "en-US"));
        Assert.Equal("- Next action: 保存设置", UiText.Translate("- 下一步：保存设置", "en-US"));
        const string note = "我的原始复盘正文，不应自动重写。";
        Assert.Equal(note, UiText.Translate(note, "en-US"));
        Assert.Equal("EURUSD.s 1.23 -33.82", UiText.Translate("EURUSD.s 1.23 -33.82", "en-US"));
        Assert.Equal("zh-CN", UiText.NormalizeLanguage("unsupported"));
    }
}
