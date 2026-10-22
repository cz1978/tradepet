using System.Reflection;
using System.Text;
using System.Text.Json;
using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Core.Pet;
using TradePet.Core.Protocol;
using Xunit;

namespace TradePet.App.Tests;

public sealed class CompanionPreferencesTests
{
    [Fact]
    public void LegacySettingsDefaultToSilentAndEveryChoiceSurvivesSerialization()
    {
        var type = typeof(TradePetRuntime).GetNestedType("DesktopSettings", BindingFlags.NonPublic)!;
        var legacy = JsonSerializer.Deserialize("{}", type, ProtocolJson.Options)!;
        Assert.Equal(true, type.GetProperty("PetTapResponseEnabled")!.GetValue(legacy));
        Assert.Equal(true, type.GetProperty("PetCompletionFeedbackEnabled")!.GetValue(legacy));
        Assert.Equal(true, type.GetProperty("PetIdleAnimationsEnabled")!.GetValue(legacy));
        Assert.Equal(false, type.GetProperty("PetSoundEnabled")!.GetValue(legacy));
        var choices = JsonSerializer.Deserialize("""{"petTapResponseEnabled":false,"petCompletionFeedbackEnabled":false,"petIdleAnimationsEnabled":false,"petSoundEnabled":true,"petSoundVolume":0.4}""", type, ProtocolJson.Options)!;
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(choices, type, ProtocolJson.Options), type, ProtocolJson.Options)!;
        foreach (var name in new[] { "PetTapResponseEnabled", "PetCompletionFeedbackEnabled", "PetIdleAnimationsEnabled" })
            Assert.Equal(false, type.GetProperty(name)!.GetValue(restored));
        Assert.Equal(true, type.GetProperty("PetSoundEnabled")!.GetValue(restored));
        Assert.Equal(0.4, type.GetProperty("PetSoundVolume")!.GetValue(restored));
    }

    [Fact]
    public void VolumeRejectsNonFiniteAndOutOfRangeSettings()
    {
        var vm = new MainViewModel { PetSoundVolume = double.NaN };
        Assert.Equal(0.25, vm.PetSoundVolume);
        vm.PetSoundVolume = -1; Assert.Equal(0, vm.PetSoundVolume);
        vm.PetSoundVolume = 4; Assert.Equal(1, vm.PetSoundVolume);
        Assert.False(vm.PetSoundEnabled);
    }

    [Theory]
    [InlineData(CompanionSound.Touch)]
    [InlineData(CompanionSound.Land)]
    [InlineData(CompanionSound.Saved)]
    [InlineData(CompanionSound.Completed)]
    public void SoundClipsAreShortPcmWithLocalVolumeAndSilentZeroVolume(CompanionSound sound)
    {
        var normal = CompanionSoundPlayer.CreateWave(sound, 1);
        var quiet = CompanionSoundPlayer.CreateWave(sound, 0.25);
        var muted = CompanionSoundPlayer.CreateWave(sound, 0);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(normal, 0, 4));
        Assert.Equal(normal.Length - 8, BitConverter.ToInt32(normal, 4));
        Assert.Equal(22050, BitConverter.ToInt32(normal, 24));
        Assert.InRange(normal.Length, 44, 22050);
        var peak = 0;
        for (var i = 44; i < normal.Length; i += 2)
        {
            var sample = Math.Abs((int)BitConverter.ToInt16(normal, i));
            peak = Math.Max(peak, sample);
            Assert.InRange(Math.Abs((int)BitConverter.ToInt16(quiet, i)), 0, sample / 4 + 1);
            Assert.Equal(0, BitConverter.ToInt16(muted, i));
        }
        Assert.InRange(peak, 1, 6000);
    }
}
