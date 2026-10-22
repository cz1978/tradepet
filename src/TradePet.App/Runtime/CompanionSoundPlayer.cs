using System.IO;
using System.Media;
using System.Text;
using TradePet.Core.Pet;

namespace TradePet.App.Runtime;

public sealed class CompanionSoundPlayer : IDisposable
{
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    public void Play(CompanionSound sound, double volume)
    {
        Stop();
        if (sound == CompanionSound.None || !double.IsFinite(volume) || volume <= 0) return;
        try
        {
            _stream = new MemoryStream(CreateWave(sound, volume), writable: false);
            _player = new SoundPlayer(_stream);
            _player.Load();
            _player.Play();
        }
        catch (Exception exception)
        {
            AppLog.Write($"Companion sound unavailable: {exception.Message}");
            Stop();
        }
    }

    // Volume is applied to this short PCM clip, never to the system mixer.
    public static byte[] CreateWave(CompanionSound sound, double volume)
    {
        const int rate = 22050;
        var notes = sound switch
        {
            CompanionSound.Touch => new[] { 660d },
            CompanionSound.Land => new[] { 330d },
            CompanionSound.Saved => new[] { 660d, 880d },
            CompanionSound.Completed => new[] { 523.25, 659.25, 783.99 },
            _ => Array.Empty<double>(),
        };
        volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 0;
        var samplesPerNote = (int)(rate * 0.14);
        var sampleCount = notes.Length * samplesPerNote;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + sampleCount * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(sampleCount * 2);
        foreach (var frequency in notes)
            for (var i = 0; i < samplesPerNote; i++)
            {
                var phase = (double)i / (samplesPerNote - 1);
                var envelope = Math.Sin(Math.PI * phase) * Math.Exp(-3 * phase);
                var sample = Math.Sin(2 * Math.PI * frequency * i / rate) * envelope * volume * 0.18;
                writer.Write((short)(sample * short.MaxValue));
            }
        writer.Flush();
        return buffer.ToArray();
    }

    public void Stop()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _stream?.Dispose();
        _stream = null;
    }
    public void Dispose() => Stop();
}
