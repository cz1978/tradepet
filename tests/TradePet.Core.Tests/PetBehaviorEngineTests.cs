using TradePet.Core.Domain;
using TradePet.Core.Pet;
using Xunit;

namespace TradePet.Core.Tests;

public sealed class PetBehaviorEngineTests
{
    [Theory]
    [InlineData(PetSignal.ProfitClosed)]
    [InlineData(PetSignal.LossClosed)]
    public void ClosedTrade_ReceivesCalmCompanyWithoutBlockingSpeech(PetSignal signal)
    {
        var engine = new PetBehaviorEngine();
        var now = DateTimeOffset.UtcNow;
        var state = engine.ApplySignal(engine.CreateInitial(now), signal, now);

        Assert.Equal(PetMood.Calm, state.Current.Mood);
        Assert.Equal(PetActivity.Waiting, state.Current.Activity);
        Assert.Null(state.Current.ActiveAlertPriority);
        Assert.Equal(TradeContextState.Flat, state.Current.TradeContext);

        var next = engine.Advance(state, now.AddSeconds(8));
        Assert.NotEqual(PetActivity.Waiting, next.Current.Activity);
    }

    [Theory]
    [InlineData(PetSignal.ProfitClosed)]
    [InlineData(PetSignal.LossClosed)]
    public void ClosedTrade_DoesNotReplaceAnActiveRiskAlert(PetSignal signal)
    {
        var engine = new PetBehaviorEngine();
        var now = DateTimeOffset.UtcNow;
        var state = engine.ApplySignal(engine.CreateInitial(now), PetSignal.Risk, now);
        state = engine.ApplySignal(state, signal, now.AddSeconds(1));

        Assert.Equal(AlertPriority.Critical, state.Current.ActiveAlertPriority);
        Assert.Equal(PetMood.Concerned, state.Current.Mood);
        Assert.Equal(PetActivity.Failed, state.Current.Activity);
    }

    [Fact]
    public void RiskInterruptsActivityAndHoldingResumesAsReview()
    {
        var engine = new PetBehaviorEngine();
        var now = DateTimeOffset.UtcNow;
        var state = engine.ApplySignal(engine.CreateInitial(now), PetSignal.Connected, now);
        state = engine.ApplySignal(state, PetSignal.TradeOpened, now.AddSeconds(1));
        state = engine.ApplySignal(state, PetSignal.Risk, now.AddSeconds(2));

        Assert.Equal(PetActivity.Failed, state.Current.Activity);
        Assert.Equal(TradeContextState.Holding, state.Current.TradeContext);
        Assert.Equal(AlertPriority.Critical, state.Current.ActiveAlertPriority);

        state = engine.CompleteInterruption(state, now.AddSeconds(3));
        Assert.Equal(PetActivity.Review, state.Current.Activity);
        Assert.Equal(PetMood.Attentive, state.Current.Mood);
        Assert.Null(state.Current.ActiveAlertPriority);
    }

    [Fact]
    public void DisconnectionDoesNotStopPetLife()
    {
        var engine = new PetBehaviorEngine();
        var now = DateTimeOffset.UtcNow;
        var state = engine.CreateInitial(now);
        state = engine.Advance(state with { Current = state.Current with { TradeContext = TradeContextState.Flat } }, now.AddSeconds(1));
        var activity = state.Current.Activity;

        state = engine.ApplySignal(state, PetSignal.Disconnected, now.AddSeconds(2));

        Assert.Equal(activity, state.Current.Activity);
        Assert.Equal(TradeContextState.Disconnected, state.Current.TradeContext);
    }
}
