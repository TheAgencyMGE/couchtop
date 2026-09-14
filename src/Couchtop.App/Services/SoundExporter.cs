using System.IO;

namespace Couchtop.App.Services;

/// <summary>Writes the synthesized sounds to 16-bit WAV files (used for trailers and documentation).</summary>
public static class SoundExporter
{
    public static void ExportAll(string directory)
    {
        Directory.CreateDirectory(directory);
        var bank = SoundSynth.Create();
        foreach (var (effect, samples) in bank.Effects)
            Write(Path.Combine(directory, effect.ToString().ToLowerInvariant() + ".wav"), samples);
        Write(Path.Combine(directory, "ambience.wav"), bank.Ambience);
    }

    public static void Write(string path, float[] stereo)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataBytes = stereo.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(SoundSynth.Rate);
        writer.Write(SoundSynth.Rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in stereo)
            writer.Write((short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue));
    }
}
