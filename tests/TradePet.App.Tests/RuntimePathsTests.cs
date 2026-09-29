using System.IO;
using System.Reflection;
using TradePet.App.Runtime;
using Xunit;

namespace TradePet.App.Tests;

public sealed class RuntimePathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tradepet-paths-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PortableRuntime_TakesPrecedenceOverExistingUserEnvironment(bool interpreterPresent)
    {
        var bundled = Path.Combine(_root, "app", "Runtime", "python-runtime", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
        if (interpreterPresent) File.WriteAllText(bundled, "");
        CreateUserPython();

        var paths = Resolve();

        Assert.Equal(bundled, Property<string>(paths, "PythonExecutable"));
        Assert.True(Property<bool>(paths, "PythonIsBundled"));
    }

    [Fact]
    public void DevelopmentBuild_StillUsesUserEnvironment()
    {
        var python = CreateUserPython();
        var paths = Resolve();
        Assert.Equal(python, Property<string>(paths, "PythonExecutable"));
        Assert.False(Property<bool>(paths, "PythonIsBundled"));
    }

    private string CreateUserPython()
    {
        var python = Path.Combine(_root, "data", "python", "venv", "Scripts", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(python)!);
        File.WriteAllText(python, "");
        return python;
    }

    private object Resolve() => typeof(TradePetRuntime).Assembly
        .GetType("TradePet.App.Runtime.RuntimePaths")!
        .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static)!
        .Invoke(null, [Path.Combine(_root, "app"), Path.Combine(_root, "data")])!;

    private static T Property<T>(object paths, string name) => (T)paths.GetType().GetProperty(name)!.GetValue(paths)!;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
