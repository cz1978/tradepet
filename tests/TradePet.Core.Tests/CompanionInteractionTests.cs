using TradePet.Core.Pet;
using Xunit;

namespace TradePet.Core.Tests;

public sealed class CompanionInteractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CompanionTrigger.Tap)]
    [InlineData(CompanionTrigger.Landed)]
    [InlineData(CompanionTrigger.Hover)]
    [InlineData(CompanionTrigger.EntrySaved)]
    [InlineData(CompanionTrigger.ScreenshotSaved)]
    [InlineData(CompanionTrigger.ReviewCompleted)]
    [InlineData(CompanionTrigger.Idle)]
    public void EachCategoryCanBeDisabledWithoutConsumingTheNextInteraction(CompanionTrigger trigger)
    {
        var engine = new CompanionInteractionEngine();
        var disabled = trigger switch
        {
            CompanionTrigger.Tap or CompanionTrigger.Landed or CompanionTrigger.Hover => new CompanionOptions(TapEnabled: false),
            CompanionTrigger.Idle => new CompanionOptions(IdleEnabled: false),
            _ => new CompanionOptions(FeedbackEnabled: false),
        };
        Assert.Null(engine.TryReact(trigger, disabled, Now, false, true, false));
        var time = trigger == CompanionTrigger.Idle ? Now.AddSeconds(90) : Now;
        Assert.NotNull(engine.TryReact(trigger, new(), time, false, true, false));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void QuietHiddenAndBusyStatesSuppressAllNewReactions(bool quiet, bool visible, bool busy)
    {
        foreach (var trigger in Enum.GetValues<CompanionTrigger>())
            Assert.Null(new CompanionInteractionEngine().TryReact(trigger, new(SoundEnabled: true), Now, quiet, visible, busy));
    }

    [Fact]
    public void ClickSupersedesHoverAndSavedFeedbackSupersedesClickButRepeatedClicksAreThrottled()
    {
        var engine = new CompanionInteractionEngine();
        Assert.NotNull(engine.TryReact(CompanionTrigger.Hover, new(), Now, false, true, false));
        Assert.Equal(CompanionAnimation.Wave, engine.TryReact(CompanionTrigger.Tap, new(), Now.AddMilliseconds(100), false, true, false)!.Animation);
        Assert.Null(engine.TryReact(CompanionTrigger.Tap, new(), Now.AddSeconds(1), false, true, false));
        Assert.Equal(CompanionAnimation.Nod, engine.TryReact(CompanionTrigger.EntrySaved, new(), Now.AddSeconds(1), false, true, false)!.Animation);
        Assert.NotNull(engine.TryReact(CompanionTrigger.Tap, new(), Now.AddSeconds(5), false, true, false));
    }

    [Fact]
    public void IdleMomentsAreSpacedAndCycleThroughAllFourActionsWithoutSound()
    {
        var engine = new CompanionInteractionEngine();
        var options = new CompanionOptions(SoundEnabled: true);
        Assert.Null(engine.TryReact(CompanionTrigger.Idle, options, Now, false, true, false));
        Assert.Null(engine.TryReact(CompanionTrigger.Idle, options, Now.AddSeconds(89), false, true, false));
        var expected = new[] { CompanionAnimation.Tea, CompanionAnimation.Read, CompanionAnimation.Doze, CompanionAnimation.Stretch };
        for (var i = 0; i < expected.Length; i++)
        {
            var time = Now.AddSeconds(90 + 120 * i);
            var reaction = engine.TryReact(CompanionTrigger.Idle, options, time, false, true, false)!;
            Assert.Equal(expected[i], reaction.Animation);
            Assert.Equal(CompanionSound.None, reaction.Sound);
            Assert.InRange(reaction.Duration.TotalSeconds, 1, 6);
            Assert.Null(engine.TryReact(CompanionTrigger.Idle, options, time.AddSeconds(60), false, true, false));
        }
    }

    [Fact]
    public void SoundsRequireOptInAndPositiveVolumeAndNeverSurviveDisabledFeedback()
    {
        CompanionReaction? Reaction(CompanionOptions options) => new CompanionInteractionEngine()
            .TryReact(CompanionTrigger.ReviewCompleted, options, Now, false, true, false);
        Assert.Equal(CompanionSound.None, Reaction(new())!.Sound);
        Assert.Equal(CompanionSound.None, Reaction(new(SoundEnabled: true, Volume: 0))!.Sound);
        Assert.Equal(CompanionSound.Completed, Reaction(new(SoundEnabled: true))!.Sound);
        Assert.Null(Reaction(new(FeedbackEnabled: false, SoundEnabled: true)));
    }
}
