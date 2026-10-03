using System.Net;
using System.Net.Http;
using System.Text.Json;
using TradePet.App.Runtime;
using Xunit;

namespace TradePet.App.Tests;

public sealed class GitHubReleaseCheckerTests
{
    [Theory]
    [InlineData("1.0.0-rc.9", "v1.0.0-rc.10", true)]
    [InlineData("1.0.0-rc.10", "v1.0.0-rc.9", false)]
    [InlineData("1.0.0-rc.7", "v1.0.0", true)]
    [InlineData("1.0.0", "v1.1.0-rc.1", false)]
    [InlineData("1.0.0-rc.7", "v1.0.0-rc.7", false)]
    public void VersionComparison_HandlesNumericPrereleasesAndStableChannel(string current, string tag, bool update)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[] { Release(tag) }));
        var result = GitHubReleaseChecker.FindUpdate(json.RootElement, current);
        Assert.Equal(update, result.HasUpdate);
    }

    [Fact]
    public void ReleaseSelection_IgnoresDraftsAndMissingOrUnfinishedWindowsPackages()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new[]
        {
            Release("v2.0.0", draft: true), Release("v1.9.0", package: "source.zip"),
            Release("v1.8.0", state: "new"), Release("v1.0.0-rc.10"), Release("v1.0.0-rc.8"),
        }));
        var result = GitHubReleaseChecker.FindUpdate(json.RootElement, "1.0.0-rc.7");
        Assert.True(result.HasUpdate);
        Assert.Equal("1.0.0-rc.10", result.LatestVersion);
        Assert.Equal("https://github.com/cz1978/tradepet/releases/tag/v1.0.0-rc.10", result.ReleasePage.AbsoluteUri);
    }

    [Fact]
    public async Task Check_UsesPublicGitHubEndpointAndRejectsFailedRequests()
    {
        var handler = new ReleaseHandler();
        using var client = new HttpClient(handler);
        using var checker = new GitHubReleaseChecker(client, "1.0.0-rc.7");
        Assert.True((await checker.CheckAsync()).HasUpdate);
        Assert.Equal("api.github.com", handler.Uri!.Host);
        Assert.Contains("TradePet", handler.UserAgent);
        handler.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => checker.CheckAsync());
    }

    private static object Release(string tag, bool draft = false, string package = "TradePet-1.0.0-win-x64.zip", string state = "uploaded") =>
        new { tag_name = tag, draft, html_url = "https://untrusted.invalid", assets = new[] { new { name = package, state } } };

    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string UserAgent { get; private set; } = "";
        public bool Fail { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new[] { Release("v1.0.0-rc.10") })),
            });
        }
    }
}
