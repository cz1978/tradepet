using TradePet.App.Runtime;
using TradePet.App.ViewModels;
using TradePet.Core.Localization;
using TradePet.Core.Pet;

namespace TradePet.App.Views;

public partial class PetWindow
{
    private readonly CompanionSoundPlayer _companionSounds = new();
    private CompanionReaction? _shownCompanionReaction;

    private void InitializeCompanion()
    {
        PetSprite.CompanionReactionEnded += CompanionReactionEnded;
        PetSprite.SetAmbientAnimationEnabled(_viewModel.PetIdleAnimationsEnabled && !_viewModel.IsFocusMode);
    }

    private async void RequestCompanionInteraction(CompanionTrigger trigger)
    {
        try { if (_viewModel.CompanionInteractionAsync is { } request) await request(trigger); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Write($"Companion interaction failed: {exception.Message}"); }
    }

    private void HandleCompanionPropertyChanged(string? property)
    {
        if (property == nameof(MainViewModel.CompanionReaction))
        {
            var reaction = _viewModel.CompanionReaction;
            if (reaction is null || !IsVisible || _isDragging || _fullscreenActive || _trayResourcesDisposed ||
                _viewModel.IsFocusMode || !_viewModel.GetCompanionOptions().Allows(reaction.Trigger)) return;
            _shownCompanionReaction = reaction;
            PetSprite.PlayCompanionReaction(reaction);
            CompanionCaption.Text = UiText.Translate(reaction.Caption);
            CompanionCaptionPopup.IsOpen = reaction.Caption.Length > 0 && !ReviewCardPopup.IsOpen;
            if (_viewModel.PetSoundEnabled) _companionSounds.Play(reaction.Sound, _viewModel.PetSoundVolume);
            return;
        }
        if (property is nameof(MainViewModel.IsFocusMode) or nameof(MainViewModel.PetTapResponseEnabled) or
            nameof(MainViewModel.PetCompletionFeedbackEnabled) or nameof(MainViewModel.PetIdleAnimationsEnabled))
        {
            PetSprite.SetAmbientAnimationEnabled(_viewModel.PetIdleAnimationsEnabled && !_viewModel.IsFocusMode);
            if (_viewModel.IsFocusMode || _shownCompanionReaction is { } current && !_viewModel.GetCompanionOptions().Allows(current.Trigger))
                StopCompanionReaction();
        }
        if (property is nameof(MainViewModel.PetSoundEnabled) or nameof(MainViewModel.PetSoundVolume)) _companionSounds.Stop();
        if (property == nameof(MainViewModel.IsBubbleVisible) && _viewModel.IsBubbleVisible ||
            property == nameof(MainViewModel.IsAlertActionVisible) && _viewModel.IsAlertActionVisible) StopCompanionReaction();
    }

    private void CompanionReactionEnded()
    {
        CompanionCaptionPopup.IsOpen = false;
        _shownCompanionReaction = null;
        _companionSounds.Stop();
    }

    private void StopCompanionReaction()
    {
        PetSprite.ClearCompanionReaction();
        CompanionReactionEnded();
    }
}
