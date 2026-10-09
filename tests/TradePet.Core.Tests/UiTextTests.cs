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
        Assert.Equal("66 trades analyzed automatically · Daily summary completed",
            UiText.Translate("已自动分析 66 笔 · 日总结已完成", "en-US"));
        Assert.Equal("Individual reviews (optional): 0/66 saved",
            UiText.Translate("逐笔复盘（可选）：已保存 0/66 笔", "en-US"));
        Assert.Equal("Exit execution report: No preset exit rules; exit state report: Feared giving back profit",
            UiText.Translate("退出执行自报：未预设退出规则；平仓状态自报：怕利润回吐", "en-US"));
        Assert.Equal("Exit reports: followed rules 1 · deviated 2 · no preset 3 · unsure 4; missing reports are not inferred.",
            UiText.Translate("退出自报：按规则 1 笔 · 偏离 2 笔 · 未预设 3 笔 · 不确定 4 笔；未填写不推断。", "en-US"));
    }
}
