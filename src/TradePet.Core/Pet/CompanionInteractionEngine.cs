namespace TradePet.Core.Pet;

public enum CompanionTrigger { Tap, Landed, Hover, EntrySaved, ScreenshotSaved, ReviewCompleted, Idle }
public enum CompanionAnimation { Wave, Tilt, Stretch, Nod, Celebrate, Tea, Read, Doze }
public enum CompanionSound { None, Touch, Land, Saved, Completed }

public sealed record CompanionOptions(bool TapEnabled = true, bool FeedbackEnabled = true,
    bool IdleEnabled = true, bool SoundEnabled = false, double Volume = 0.25)
{
    public bool Allows(CompanionTrigger trigger) => trigger switch
    {
        CompanionTrigger.Tap or CompanionTrigger.Landed or CompanionTrigger.Hover => TapEnabled,
        CompanionTrigger.Idle => IdleEnabled,
        _ => FeedbackEnabled,
    };
}

public sealed record CompanionReaction(long Id, CompanionTrigger Trigger, CompanionAnimation Animation,
    TimeSpan Duration, string Caption, CompanionSound Sound);

public sealed class CompanionInteractionEngine
{
    private DateTimeOffset _lastReaction = DateTimeOffset.MinValue;
    private DateTimeOffset _lastHover = DateTimeOffset.MinValue;
    private DateTimeOffset _nextIdle = DateTimeOffset.MinValue;
    private int _tapIndex;
    private int _idleIndex;
    private long _sequence;
    private CompanionTrigger _lastTrigger = CompanionTrigger.Idle;

    public CompanionReaction? TryReact(CompanionTrigger trigger, CompanionOptions options,
        DateTimeOffset now, bool quiet, bool visible, bool busy)
    {
        if (!options.Allows(trigger) || quiet || !visible || busy)
        {
            if (trigger == CompanionTrigger.Idle) _nextIdle = now.AddSeconds(90);
            return null;
        }
        if (trigger == CompanionTrigger.Idle && _nextIdle == DateTimeOffset.MinValue)
        {
            _nextIdle = now.AddSeconds(90);
            return null;
        }
        if (now - _lastReaction < TimeSpan.FromSeconds(3) && Priority(trigger) <= Priority(_lastTrigger) ||
            trigger == CompanionTrigger.Hover && now - _lastHover < TimeSpan.FromSeconds(30) ||
            trigger == CompanionTrigger.Idle && now < _nextIdle) return null;

        _lastReaction = now;
        _lastTrigger = trigger;
        _nextIdle = now.AddSeconds(90 + (int)(_sequence % 3) * 15);
        var animation = CompanionAnimation.Nod;
        var sound = CompanionSound.None;
        var caption = "";
        var duration = TimeSpan.FromSeconds(2.4);
        switch (trigger)
        {
            case CompanionTrigger.Tap:
                animation = _tapIndex++ % 2 == 0 ? CompanionAnimation.Wave : CompanionAnimation.Tilt;
                caption = _tapIndex % 3 == 1 ? "我在，慢慢来。" : "";
                sound = CompanionSound.Touch;
                break;
            case CompanionTrigger.Hover:
                _lastHover = now;
                animation = CompanionAnimation.Tilt;
                break;
            case CompanionTrigger.Landed:
                animation = CompanionAnimation.Stretch;
                sound = CompanionSound.Land;
                break;
            case CompanionTrigger.EntrySaved:
            case CompanionTrigger.ScreenshotSaved:
                caption = "记好了，给认真记录的你盖个章。";
                sound = CompanionSound.Saved;
                break;
            case CompanionTrigger.ReviewCompleted:
                animation = CompanionAnimation.Celebrate;
                caption = "复盘完成，今天又多懂了自己一点。";
                sound = CompanionSound.Completed;
                duration = TimeSpan.FromSeconds(3);
                break;
            case CompanionTrigger.Idle:
                animation = (_idleIndex++ % 4) switch
                {
                    0 => CompanionAnimation.Tea, 1 => CompanionAnimation.Read,
                    2 => CompanionAnimation.Doze, _ => CompanionAnimation.Stretch,
                };
                duration = TimeSpan.FromSeconds(5);
                break;
        }
        return new(++_sequence, trigger, animation, duration, caption,
            options.SoundEnabled && options.Volume > 0 ? sound : CompanionSound.None);
    }

    private static int Priority(CompanionTrigger trigger) => trigger switch
    {
        CompanionTrigger.Idle => 0, CompanionTrigger.Hover => 1,
        CompanionTrigger.Tap or CompanionTrigger.Landed => 2, _ => 3,
    };
}
