using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Core.Protocol;
using TradePet.Infrastructure.Mt5;
using Xunit;

namespace TradePet.App.Tests;

public sealed class SetupSettingsTests
{
    [Fact]
    public void LegacyDesktopSettings_RequestSetupAndKeepMt5Default()
    {
        var type = typeof(TradePetRuntime).GetNestedType("DesktopSettings", BindingFlags.NonPublic)!;
        var oldJson = """{"focusMode":false,"topmost":true,"positionLocked":false,"mouseThrough":false,"opacity":1,"scale":0.7,"lossZoneTolerance":200,"terminalPath":"C:\\Broker\\terminal64.exe"}""";
        var settings = JsonSerializer.Deserialize(oldJson, type, ProtocolJson.Options)!;
        Assert.Equal(0, type.GetProperty("SetupVersion")!.GetValue(settings));
        Assert.Equal(TradingPlatform.Mt5, type.GetProperty("Platform")!.GetValue(settings));
        Assert.Equal("zh-CN", type.GetProperty("UiLanguage")!.GetValue(settings));
        foreach (var name in new[] { "QuickReviewPromptEnabled", "EntryReasonPromptEnabled", "UpdateNotificationsEnabled" })
            Assert.Equal(true, type.GetProperty(name)!.GetValue(settings));
        var json = JsonSerializer.SerializeToNode(settings, type, ProtocolJson.Options)!;
        json["platform"] = "mt4";
        json["setupVersion"] = 1;
        json["terminalPath"] = @"D:\MT4\terminal.exe";
        var restored = JsonSerializer.Deserialize(json.ToJsonString(), type, ProtocolJson.Options)!;
        Assert.Equal(1, type.GetProperty("SetupVersion")!.GetValue(restored));
        Assert.Equal(TradingPlatform.Mt4, type.GetProperty("Platform")!.GetValue(restored));
        Assert.Equal(@"D:\MT4\terminal.exe", type.GetProperty("TerminalPath")!.GetValue(restored));
    }

    [Fact]
    public void PromptPreferences_DefaultOnAndExplicitOffSurvivesReload()
    {
        var vm = new MainViewModel();
        Assert.True(vm.QuickReviewPromptEnabled);
        Assert.True(vm.EntryReasonPromptEnabled);
        var type = typeof(TradePetRuntime).GetNestedType("DesktopSettings", BindingFlags.NonPublic)!;
        var settings = JsonSerializer.Deserialize("""{"quickReviewPromptEnabled":false,"entryReasonPromptEnabled":false,"updateNotificationsEnabled":false}""", type, ProtocolJson.Options)!;
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, type, ProtocolJson.Options), type, ProtocolJson.Options)!;
        foreach (var name in new[] { "QuickReviewPromptEnabled", "EntryReasonPromptEnabled", "UpdateNotificationsEnabled" })
            Assert.Equal(false, type.GetProperty(name)!.GetValue(restored));
    }

    [Fact]
    public async Task UiLanguage_IsPersistedWithDesktopSettingsAndLoadedBeforeWindowCreation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tradepet-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tradepet.db");
        try
        {
            var database = new TradePet.Infrastructure.Persistence.AppDatabase(path);
            await database.InitializeAsync();
            Assert.Equal("zh-CN", await TradePet.App.Localization.UiLanguagePreference.LoadAsync(path));
            var settingsType = typeof(TradePetRuntime).GetNestedType("DesktopSettings", BindingFlags.NonPublic)!;
            var settings = JsonSerializer.Deserialize("""{"uiLanguage":"en-US","quickReviewPromptEnabled":false,"platform":"mt4"}""", settingsType, ProtocolJson.Options)!;
            await database.SaveSettingAsync("global", "desktop", settings);
            Assert.Equal("en-US", await TradePet.App.Localization.UiLanguagePreference.LoadAsync(path));
            var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, settingsType, ProtocolJson.Options), settingsType, ProtocolJson.Options)!;
            Assert.Equal(false, settingsType.GetProperty("QuickReviewPromptEnabled")!.GetValue(restored));
            Assert.Equal(TradingPlatform.Mt4, settingsType.GetProperty("Platform")!.GetValue(restored));
            var vm = new MainViewModel { UiLanguage = "en-GB" };
            Assert.Equal("en-US", vm.UiLanguage);
            Assert.Equal(new[] { "zh-CN", "en-US" }, vm.UiLanguageOptions.Select(option => option.Code));
            vm.UiLanguage = "unsupported";
            Assert.Equal("zh-CN", vm.UiLanguage);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PlatformChange_DropsIncompatibleTerminalWithoutClaimingSetupIsComplete()
    {
        var vm = new MainViewModel { SelectedTerminalPath = @"C:\MT5\terminal64.exe" };
        vm.SelectedPlatform = TradingPlatform.Mt4;
        Assert.Null(vm.SelectedTerminalPath);
        Assert.True(vm.NeedsSetup);
        var converter = new HistoryMetricConverter();
        Assert.Equal("—", converter.Convert([false, 0m], typeof(string), "0.##", CultureInfo.InvariantCulture));
        Assert.Equal("0", converter.Convert([true, 0m], typeof(string), "0.##", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MonitoringSession_CanBecomeLiveWithoutClaimingDealHistoryWasImported()
    {
        var session = new TradePet.Core.Session.WorkerSession();
        session.Connect("MT4:Broker|42");
        session.MarkSnapshotReceived(requiresDealHistory: false);
        Assert.Equal(TradePet.Core.Session.WorkerSessionPhase.Live, session.Phase);
        Assert.False(session.HasInitialDeals);
        session.Disconnect();
        session.MarkSnapshotReceived(requiresDealHistory: false);
        Assert.Equal(TradePet.Core.Session.WorkerSessionPhase.Disconnected, session.Phase);
    }
}
