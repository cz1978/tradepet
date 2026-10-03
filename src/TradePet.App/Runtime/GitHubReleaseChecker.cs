using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TradePet.App.Runtime;

public sealed record GitHubReleaseCheck(string CurrentVersion, string? LatestVersion, Uri ReleasePage, bool HasUpdate);

public sealed class GitHubReleaseChecker : IDisposable
{
    public const string ReleasesUrl = "https://github.com/cz1978/tradepet/releases";
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _currentVersion;

    public static string CurrentVersion => typeof(GitHubReleaseChecker).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(GitHubReleaseChecker).Assembly.GetName().Version!.ToString(3);

    public GitHubReleaseChecker(HttpClient? client = null, string? currentVersion = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _currentVersion = currentVersion ?? CurrentVersion;
    }

    public async Task<GitHubReleaseCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/cz1978/tradepet/releases?per_page=30");
        request.Headers.UserAgent.ParseAdd("TradePet/" + _currentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return FindUpdate(document.RootElement, _currentVersion);
    }

    public static GitHubReleaseCheck FindUpdate(JsonElement releases, string currentVersion)
    {
        var current = ReleaseVersion.Parse(currentVersion)
            ?? throw new InvalidOperationException("无法识别当前版本号。");
        ReleaseVersion? latest = null;
        string? latestTag = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (!release.TryGetProperty("tag_name", out var tagProperty)) continue;
            var tag = tagProperty.GetString();
            var version = ReleaseVersion.Parse(tag);
            if (version is null || !current.IsPrerelease && version.IsPrerelease) continue;
            if (!release.TryGetProperty("assets", out var assets) ||
                !assets.EnumerateArray().Any(asset =>
                    asset.TryGetProperty("name", out var name) &&
                    name.GetString()?.StartsWith("TradePet-", StringComparison.OrdinalIgnoreCase) == true &&
                    name.GetString()!.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) &&
                    (!asset.TryGetProperty("state", out var state) || state.GetString() == "uploaded"))) continue;
            if (latest is not null && version.CompareTo(latest) <= 0) continue;
            latest = version;
            latestTag = tag;
        }
        return new GitHubReleaseCheck(currentVersion, latestTag?.TrimStart('v', 'V'),
            new Uri(latestTag is null ? ReleasesUrl : ReleasesUrl + "/tag/" + Uri.EscapeDataString(latestTag)),
            latest is not null && latest.CompareTo(current) > 0);
    }

    private sealed record ReleaseVersion(int Major, int Minor, int Patch, string[] Prerelease) : IComparable<ReleaseVersion>
    {
        public bool IsPrerelease => Prerelease.Length > 0;
        public static ReleaseVersion? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var match = Regex.Match(text, @"^[vV]?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) ||
                !int.TryParse(match.Groups[2].Value, out var minor) || !int.TryParse(match.Groups[3].Value, out var patch)) return null;
            return new(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value.Split('.') : []);
        }
        public int CompareTo(ReleaseVersion? other)
        {
            if (other is null) return 1;
            var result = Major.CompareTo(other.Major);
            if (result == 0) result = Minor.CompareTo(other.Minor);
            if (result == 0) result = Patch.CompareTo(other.Patch);
            if (result != 0) return result;
            if (!IsPrerelease || !other.IsPrerelease) return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
            for (var index = 0; index < Math.Min(Prerelease.Length, other.Prerelease.Length); index++)
            {
                var left = Prerelease[index]; var right = other.Prerelease[index];
                var leftNumeric = left.All(char.IsAsciiDigit); var rightNumeric = right.All(char.IsAsciiDigit);
                if (leftNumeric && rightNumeric)
                {
                    left = left.TrimStart('0'); right = right.TrimStart('0');
                    result = left.Length.CompareTo(right.Length);
                    if (result == 0) result = string.CompareOrdinal(left, right);
                }
                else result = leftNumeric == rightNumeric ? string.CompareOrdinal(left, right) : leftNumeric ? -1 : 1;
                if (result != 0) return result;
            }
            return Prerelease.Length.CompareTo(other.Prerelease.Length);
        }
    }

    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
