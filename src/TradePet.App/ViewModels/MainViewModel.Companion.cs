using TradePet.Core.Pet;

namespace TradePet.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _petTapResponseEnabled = true;
    private bool _petCompletionFeedbackEnabled = true;
    private bool _petIdleAnimationsEnabled = true;
    private bool _petSoundEnabled;
    private double _petSoundVolume = 0.25;
    private CompanionReaction? _companionReaction;

    public bool PetTapResponseEnabled { get => _petTapResponseEnabled; set => SetProperty(ref _petTapResponseEnabled, value); }
    public bool PetCompletionFeedbackEnabled { get => _petCompletionFeedbackEnabled; set => SetProperty(ref _petCompletionFeedbackEnabled, value); }
    public bool PetIdleAnimationsEnabled { get => _petIdleAnimationsEnabled; set => SetProperty(ref _petIdleAnimationsEnabled, value); }
    public bool PetSoundEnabled { get => _petSoundEnabled; set => SetProperty(ref _petSoundEnabled, value); }
    public double PetSoundVolume { get => _petSoundVolume; set => SetProperty(ref _petSoundVolume, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.25); }
    public CompanionReaction? CompanionReaction { get => _companionReaction; set => SetProperty(ref _companionReaction, value); }
    public Func<CompanionTrigger, Task>? CompanionInteractionAsync { get; set; }
    public CompanionOptions GetCompanionOptions() => new(PetTapResponseEnabled, PetCompletionFeedbackEnabled,
        PetIdleAnimationsEnabled, PetSoundEnabled, PetSoundVolume);
}
