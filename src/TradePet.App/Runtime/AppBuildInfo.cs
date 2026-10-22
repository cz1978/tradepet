using System.Globalization;
using System.Text.RegularExpressions;
using TradePet.Core.Localization;

namespace TradePet.App.Runtime;

public static class AppBuildInfo
{
    public static string Label => ParseLocalBuildTime(GitHubReleaseChecker.BuildVersion) is { } at
        ? string.Format(UiText.Get("LocalBuildLabel"), at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
        : string.Empty;

    public static DateTimeOffset? ParseLocalBuildTime(string version)
    {
        var match = Regex.Match(version, @"\+local\.(\d{8}T\d{6}Z)(?:\.|$)", RegexOptions.CultureInvariant);
        return match.Success && DateTimeOffset.TryParseExact(match.Groups[1].Value, "yyyyMMdd'T'HHmmss'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : null;
    }
}
