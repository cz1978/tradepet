using TradePet.Core.Pet;

namespace TradePet.App.Runtime;

public sealed partial class TradePetRuntime
{
    private readonly CompanionInteractionEngine _companionInteractions = new();

    private async Task RequestCompanionReactionAsync(CompanionTrigger trigger)
    {
        try
        {
            await OnUiAsync(() =>
            {
                var passive = trigger is CompanionTrigger.Idle or CompanionTrigger.Hover or CompanionTrigger.Tap;
                var busy = _petState.Current.ActiveAlertPriority is not null || _viewModel.IsAlertActionVisible ||
                    _viewModel.IsBubbleVisible || passive && (_entryReasonCard is not null || _quickReviewCard is not null || _behaviorActionCard is not null);
                var reaction = _companionInteractions.TryReact(trigger, _viewModel.GetCompanionOptions(),
                    _timeProvider.GetUtcNow(), _viewModel.IsFocusMode, CanShowAutomaticPrompt?.Invoke() ?? true, busy);
                if (reaction is not null) _viewModel.CompanionReaction = reaction;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Write($"Companion feedback unavailable: {exception.Message}"); }
    }
}
