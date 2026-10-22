using System.Diagnostics;
using SkiaSharp;
using TradePet.Core.Domain;
using TradePet.Core.Pet;

namespace TradePet.App.Controls;

public sealed partial class PetSpriteControl
{
    private bool _ambientAnimationEnabled = true;
    private CompanionReaction? _companionReaction;
    private readonly Stopwatch _companionClock = new();
    public bool HasCompanionReaction => _companionReaction is not null;
    private bool HasFunctionalAnimation => Activity is PetActivity.Failed or PetActivity.Waving or PetActivity.Jumping;
    public event Action? CompanionReactionEnded;

    public void SetAmbientAnimationEnabled(bool enabled)
    {
        if (_ambientAnimationEnabled == enabled) return;
        _ambientAnimationEnabled = enabled;
        ResetAnimation();
    }

    public void PlayCompanionReaction(CompanionReaction reaction)
    {
        if (_disposed) return;
        _companionReaction = reaction;
        _companionClock.Restart();
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        UpdateAnimationTimer();
        _surface.InvalidateVisual();
    }

    public void ClearCompanionReaction()
    {
        if (_companionReaction is null) return;
        _companionReaction = null;
        _companionClock.Reset();
        ResetAnimation();
        CompanionReactionEnded?.Invoke();
    }

    private bool AdvanceCompanionFrame()
    {
        if (_companionReaction is null) return false;
        if (_companionClock.Elapsed >= _companionReaction.Duration)
        {
            ClearCompanionReaction();
            return true;
        }
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        _surface.InvalidateVisual();
        return true;
    }

    private bool PaintCompanionReaction(SKCanvas canvas, int width, int height)
    {
        if (_companionReaction is not { } reaction || _atlas is null) return false;
        var seconds = (float)_companionClock.Elapsed.TotalSeconds;
        var wave = MathF.Sin(seconds * MathF.PI * 2);
        var row = 0;
        var frame = 0;
        switch (reaction.Animation)
        {
            case CompanionAnimation.Wave: row = 3; frame = (int)(seconds * 6) % 4; break;
            case CompanionAnimation.Tilt: row = 8; frame = 3; break;
            case CompanionAnimation.Stretch: row = 3; frame = 2; break;
            case CompanionAnimation.Nod: row = 8; frame = (int)(seconds * 3) % 2 == 0 ? 0 : 2; break;
            case CompanionAnimation.Celebrate: row = 4; frame = (int)(seconds * 6) % 5; break;
            case CompanionAnimation.Tea: row = 6; frame = 0; break;
            case CompanionAnimation.Read: row = 8; frame = 1; break;
            case CompanionAnimation.Doze: row = 6; frame = 2; break;
        }
        var scale = Math.Min(width / 192f, height / 208f);
        canvas.Save();
        canvas.Translate((width - 192 * scale) / 2, height - 208 * scale);
        canvas.Scale(scale);
        canvas.Save();
        if (reaction.Animation == CompanionAnimation.Tilt) canvas.RotateDegrees(5 * MathF.Sin(seconds * 2), 96, 180);
        if (reaction.Animation == CompanionAnimation.Nod) canvas.Translate(0, 2 * wave);
        if (reaction.Animation == CompanionAnimation.Celebrate) canvas.Translate(0, -7 * Math.Abs(wave));
        if (reaction.Animation is CompanionAnimation.Stretch or CompanionAnimation.Doze)
        {
            canvas.Translate(96, 204);
            canvas.Scale(1 - 0.015f * wave, 1 + 0.015f * wave);
            canvas.Translate(-96, -204);
        }
        using var spritePaint = new SKPaint { FilterQuality = SKFilterQuality.None };
        canvas.DrawBitmap(_atlas, new SKRect(frame * 192, row * 208, (frame + 1) * 192, (row + 1) * 208),
            new SKRect(0, 0, 192, 208), spritePaint);
        canvas.Restore();
        PaintCompanionProp(canvas, reaction.Animation, seconds);
        canvas.Restore();
        return true;
    }

    private static void PaintCompanionProp(SKCanvas canvas, CompanionAnimation animation, float seconds)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#D9EEE9"), StrokeWidth = 2.5f };
        if (animation == CompanionAnimation.Tea)
        {
            var cupY = 113 - 17 * (0.5f + 0.5f * MathF.Sin(seconds * 1.5f));
            paint.Style = SKPaintStyle.Stroke;
            canvas.DrawCircle(109, cupY + 7, 6, paint);
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawRoundRect(new SKRect(86, cupY, 108, cupY + 17), 4, 4, paint);
            paint.Color = SKColor.Parse("#AFCAC1");
            canvas.DrawOval(97, cupY + 1, 9, 2.5f, paint);
            paint.Color = new SKColor(220, 241, 237, 160);
            for (var i = 0; i < 2; i++)
                canvas.DrawLine(92 + i * 8, cupY - 4, 94 + i * 8 + MathF.Sin(seconds * 3), cupY - 12, paint);
        }
        else if (animation == CompanionAnimation.Read)
        {
            paint.Color = SKColor.Parse("#72AFA5");
            canvas.DrawRoundRect(new SKRect(75, 108, 118, 139), 3, 3, paint);
            paint.Color = SKColor.Parse("#FAF1DA");
            canvas.DrawRect(78, 110, 17, 25, paint); canvas.DrawRect(98, 110, 17, 25, paint);
            paint.Color = SKColor.Parse("#97A79E"); paint.StrokeWidth = 1;
            for (var i = 0; i < 3; i++)
            {
                canvas.DrawLine(81, 116 + i * 6, 92, 116 + i * 6, paint);
                canvas.DrawLine(101, 116 + i * 6, 112, 116 + i * 6, paint);
            }
        }
        else if (animation == CompanionAnimation.Doze)
        {
            paint.Color = SKColor.Parse("#BDD4E8"); paint.TextSize = 14;
            canvas.DrawText("z", 124, 52 - seconds % 2 * 5, paint);
            paint.TextSize = 19; canvas.DrawText("Z", 139, 38 - seconds % 2 * 5, paint);
        }
        else if (animation is CompanionAnimation.Nod or CompanionAnimation.Celebrate)
        {
            paint.Color = SKColor.Parse("#72D3B3"); paint.Style = SKPaintStyle.Stroke;
            canvas.DrawCircle(145, 104, 13, paint);
            canvas.DrawLine(138, 104, 143, 109, paint); canvas.DrawLine(143, 109, 153, 98, paint);
            if (animation == CompanionAnimation.Celebrate)
                for (var i = 0; i < 5; i++)
                {
                    var angle = i * MathF.PI * 2 / 5 + seconds;
                    var x = 96 + MathF.Cos(angle) * 56;
                    var y = 78 + MathF.Sin(angle) * 42;
                    canvas.DrawLine(x - 3, y, x + 3, y, paint); canvas.DrawLine(x, y - 3, x, y + 3, paint);
                }
        }
    }
}
