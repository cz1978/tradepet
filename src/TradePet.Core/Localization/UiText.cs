using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradePet.Core.Localization;

public static class UiText
{
    private static readonly Lazy<Catalog> Translations = new(LoadCatalog);
    private static string _language = "zh-CN";
    public static string Language => _language;
    public static string NormalizeLanguage(string? language) =>
        language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en-US" : "zh-CN";

    public static void Configure(string? language)
    {
        _language = NormalizeLanguage(language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(_language);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(_language);
    }

    public static string Get(string key) => Get(key, Language);
    public static string Get(string key, string language)
    {
        var entry = Translations.Value.ByKey[key];
        return NormalizeLanguage(language) == "en-US" ? entry.English : entry.Source;
    }

    public static string Translate(string? text) => Translate(text, Language);
    public static string Translate(string? text, string language)
    {
        if (string.IsNullOrEmpty(text) || NormalizeLanguage(language) != "en-US") return text ?? string.Empty;
        if (!text.Any(c => c is >= '\u3400' and <= '\u9fff')) return text;
        var translated = TranslateLine(text);
        if (translated != text || !text.Contains('\n')) return translated;
        return Regex.Replace(text, @"[^\r\n]+", match => TranslateLine(match.Value));
    }

    private static string TranslateLine(string text)
    {
        var catalog = Translations.Value;
        if (catalog.BySource.TryGetValue(text, out var exact)) return exact.English;
        foreach (var template in catalog.Templates)
        {
            if (!text.Contains(template.Anchor, StringComparison.Ordinal)) continue;
            var match = template.Pattern.Match(text);
            if (!match.Success) continue;
            return Regex.Replace(template.Entry.English, @"\{(\d+)\}", placeholder =>
            {
                var value = match.Groups["p" + placeholder.Groups[1].Value].Value;
                if (template.Entry.Preserve?.Contains(int.Parse(placeholder.Groups[1].Value, CultureInfo.InvariantCulture)) == true) return value;
                return Translate(value, "en-US");
            });
        }
        return text;
    }

    private static Catalog LoadCatalog()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("TradePet.Core.Localization.Strings.en.json")
            ?? throw new InvalidOperationException("UI translation catalog is missing.");
        var entries = JsonSerializer.Deserialize<Entry[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var templates = entries.Where(e => e.Template).Select(entry =>
        {
            var parts = Regex.Split(entry.Source, @"(\{\d+\})");
            var pattern = "\\A" + string.Concat(parts.Select(part => Regex.IsMatch(part, @"^\{\d+\}$")
                ? "(?<p" + part[1..^1] + ">.*?)" : Regex.Escape(part))) + "\\z";
            var anchor = Regex.Matches(entry.Source, @"[\u3400-\u9fff]+").Select(m => m.Value).OrderByDescending(s => s.Length).FirstOrDefault() ?? string.Empty;
            return new Template(entry, new Regex(pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)), anchor);
        }).OrderByDescending(t => t.Anchor.Length).ToArray();
        return new Catalog(entries.ToDictionary(e => e.Key, StringComparer.Ordinal),
            entries.GroupBy(e => e.Source, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal), templates);
    }

    private sealed record Entry(string Key, string Source, string English, bool Template, int[]? Preserve = null);
    private sealed record Template(Entry Entry, Regex Pattern, string Anchor);
    private sealed record Catalog(IReadOnlyDictionary<string, Entry> ByKey, IReadOnlyDictionary<string, Entry> BySource, Template[] Templates);
}
