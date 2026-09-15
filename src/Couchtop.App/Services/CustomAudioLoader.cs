using System.IO;
using NAudio.Wave;
using Couchtop.Core.Diagnostics;

namespace Couchtop.App.Services;

/// <summary>Decodes user audio files with Windows Media Foundation into Couchtop's mixer format.</summary>
internal static class CustomAudioLoader
{
    public static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(SoundSynth.Rate, 2);

    /// <summary>Reads up to <paramref name="maxSeconds"/> of a file into memory, fading out if it had to be cut.</summary>
    public static float[] LoadClip(string path, double maxSeconds)
    {
        using var reader = new MediaFoundationReader(path);
        using var resampler = new MediaFoundationResampler(reader, Format);
        var samples = resampler.ToSampleProvider();
        var max = (int)(maxSeconds * SoundSynth.Rate) * 2;
        var data = new float[max];
        var total = 0;
        while (total < max)
        {
            var read = samples.Read(data, total, Math.Min(16384, max - total));
            if (read <= 0) break;
            total += read;
        }
        if (total == 0) throw new InvalidDataException("There's no sound in that file.");
        if (total == max)
        {
            var fade = Math.Min(total / 2, (int)(Math.Min(1.0, maxSeconds / 8) * SoundSynth.Rate) * 2);
            for (var i = 0; i < fade; i++) data[total - fade + i] *= 1 - (float)i / fade;
        }
        Array.Resize(ref data, total);
        return data;
    }

    public static string Explain(Exception ex) => ex switch
    {
        FileNotFoundException or NotSupportedException or InvalidDataException => ex.Message,
        UnauthorizedAccessException => "Couchtop isn't allowed to read that file.",
        _ => "Windows can't play this file. Try an MP3 or WAV file.",
    };
}

/// <summary>Streams a music file forever, starting over at the end. Never throws on the audio thread.</summary>
internal sealed class LoopingFileSource : ISampleProvider, IDisposable
{
    private readonly MediaFoundationReader _reader;
    private MediaFoundationResampler? _resampler;
    private ISampleProvider _samples = null!;
    private bool _failed;

    public LoopingFileSource(string path)
    {
        _reader = new MediaFoundationReader(path);
        Open();
        var probe = new float[256];
        if (_samples.Read(probe, 0, probe.Length) <= 0) throw new InvalidDataException("There's no sound in that file.");
        Open();
    }

    public WaveFormat WaveFormat => CustomAudioLoader.Format;

    private void Open()
    {
        _resampler?.Dispose();
        _reader.Position = 0;
        _resampler = new MediaFoundationResampler(_reader, CustomAudioLoader.Format);
        _samples = _resampler.ToSampleProvider();
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var total = 0;
        if (!_failed)
        {
            try
            {
                var restarted = false;
                while (total < count)
                {
                    var read = _samples.Read(buffer, offset + total, count - total);
                    if (read > 0)
                    {
                        total += read;
                        restarted = false;
                        continue;
                    }
                    if (restarted) break;
                    Open();
                    restarted = true;
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                Log.Warn("Custom menu music stopped playing", ex);
            }
        }
        if (total < count) Array.Clear(buffer, offset + total, count - total);
        return count;
    }

    public void Dispose()
    {
        _resampler?.Dispose();
        _reader.Dispose();
    }
}
