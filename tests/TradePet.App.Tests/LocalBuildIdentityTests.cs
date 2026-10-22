using TradePet.App.Runtime;
using Xunit;

namespace TradePet.App.Tests;

public sealed class LocalBuildIdentityTests
{
    [Theory]
    [InlineData("1.0.0+local.20261022T025000Z")]
    [InlineData("1.0.0+local.20261022T025000Z.b003b762")]
    public void LocalBuild_PreservesItsActualCompilationTime(string version) =>
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 2, 50, 0, TimeSpan.Zero), AppBuildInfo.ParseLocalBuildTime(version));

    [Theory]
    [InlineData("1.0.0+b003b762")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+local.20261322T025000Z")]
    public void ReleaseOrInvalidBuild_DoesNotInventALocalBuildTime(string version) =>
        Assert.Null(AppBuildInfo.ParseLocalBuildTime(version));
}
