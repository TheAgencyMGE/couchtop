namespace Couchtop.App.Services;

public enum SoundEffect
{
    Hover,
    Select,
    Back,
    Page,
    Launch,
    Startup,
    Error,
    HomeOpen,
    HomeClose,
    Tick,
}

/// <summary>Stereo float PCM clips at 44.1 kHz.</summary>
public sealed class SoundBank
{
    public Dictionary<SoundEffect, float[]> Effects { get; } = new();
    public float[] Ambience { get; set; } = Array.Empty<float>();
}

/// <summary>
/// All Couchtop sounds are synthesized at runtime from scratch (original compositions, no sampled audio),
/// in a soft bell / music-box style reminiscent of early-2000s console menus.
/// </summary>
public static class SoundSynth
{
    public const int Rate = 44100;

    public static SoundBank Create(bool includeAmbience = true)
    {
        var bank = new SoundBank();
        bank.Effects[SoundEffect.Hover] = Hover();
        bank.Effects[SoundEffect.Tick] = Tick();
        bank.Effects[SoundEffect.Select] = Select();
        bank.Effects[SoundEffect.Back] = Back();
        bank.Effects[SoundEffect.Page] = Page();
        bank.Effects[SoundEffect.Launch] = Launch();
        bank.Effects[SoundEffect.Startup] = Startup();
        bank.Effects[SoundEffect.Error] = Error();
        bank.Effects[SoundEffect.HomeOpen] = Glide(420, 980, 0.13, 0.16);
        bank.Effects[SoundEffect.HomeClose] = Glide(900, 380, 0.13, 0.14);
        if (includeAmbience) bank.Ambience = Ambience();
        return bank;
    }

    private static float[] Buffer(double seconds) => new float[(int)(seconds * Rate) * 2];

    private static double Note(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>Soft bell: a few slightly inharmonic partials with exponential decay.</summary>
    private static void Bell(float[] buf, double start, double freq, double duration, double amp, double decay, double pan = 0, double brightness = 1)
    {
        var s = (int)(start * Rate);
        var n = (int)(duration * Rate);
        var left = amp * Math.Sqrt(0.5 * (1 - pan));
        var right = amp * Math.Sqrt(0.5 * (1 + pan));
        for (var i = 0; i < n; i++)
        {
            var idx = (s + i) * 2;
            if (idx + 1 >= buf.Length) break;
            var t = (double)i / Rate;
            var attack = t < 0.004 ? t / 0.004 : 1;
            var env = attack * Math.Exp(-t * decay);
            var w = 2 * Math.PI * freq * t;
            var v = Math.Sin(w)
                    + 0.32 * brightness * Math.Sin(2.0 * w) * Math.Exp(-t * decay * 0.8)
                    + 0.11 * brightness * Math.Sin(3.01 * w) * Math.Exp(-t * decay * 1.6)
                    + 0.04 * brightness * Math.Sin(4.2 * w) * Math.Exp(-t * decay * 2.5);
            v *= env;
            buf[idx] += (float)(v * left);
            buf[idx + 1] += (float)(v * right);
        }
    }

    private static void Pad(float[] buf, double start, double duration, double[] freqs, double amp)
    {
        var s = (int)(start * Rate);
        var n = (int)(duration * Rate);
        for (var i = 0; i < n; i++)
        {
            var idx = (s + i) * 2;
            if (idx + 1 >= buf.Length) break;
            var t = (double)i / Rate;
            var env = Math.Min(1, t / 0.35) * Math.Min(1, (duration - t) / 0.45);
            if (env <= 0) continue;
            double v = 0;
            for (var k = 0; k < freqs.Length; k++)
            {
                var f = freqs[k];
                v += Math.Sin(2 * Math.PI * f * t + k) + 0.15 * Math.Sin(2 * Math.PI * f * 2.003 * t);
            }
            v *= amp * env / freqs.Length;
            var wobble = 1 + 0.08 * Math.Sin(2 * Math.PI * 0.25 * t);
            buf[idx] += (float)(v * wobble);
            buf[idx + 1] += (float)(v * (2 - wobble));
        }
    }

    private static void NoiseBurst(float[] buf, double start, double duration, double amp, int seed, double lowpass)
    {
        var rng = new Random(seed);
        var s = (int)(start * Rate);
        var n = (int)(duration * Rate);
        double y = 0;
        for (var i = 0; i < n; i++)
        {
            var idx = (s + i) * 2;
            if (idx + 1 >= buf.Length) break;
            var t = (double)i / n;
            var env = Math.Sin(Math.PI * Math.Min(1, t)) * (1 - t);
            y += lowpass * ((rng.NextDouble() * 2 - 1) - y);
            var v = (float)(y * amp * env);
            buf[idx] += v;
            buf[idx + 1] += v;
        }
    }

    private static float[] Hover()
    {
        var b = Buffer(0.09);
        Bell(b, 0, 2093, 0.09, 0.09, 55, 0, 0.3);
        Bell(b, 0.004, 3136, 0.05, 0.03, 90, 0, 0.2);
        return b;
    }

    private static float[] Tick()
    {
        var b = Buffer(0.04);
        Bell(b, 0, 1568, 0.04, 0.06, 120, 0, 0.1);
        return b;
    }

    private static float[] Select()
    {
        var b = Buffer(0.55);
        Bell(b, 0, Note(84), 0.5, 0.2, 9, -0.1);
        Bell(b, 0.055, Note(91), 0.5, 0.17, 8, 0.1);
        return b;
    }

    private static float[] Back()
    {
        var b = Buffer(0.45);
        Bell(b, 0, Note(86), 0.4, 0.16, 11, 0.1);
        Bell(b, 0.06, Note(79), 0.4, 0.15, 10, -0.1);
        return b;
    }

    private static float[] Page()
    {
        var b = Buffer(0.32);
        NoiseBurst(b, 0, 0.28, 0.35, 7, 0.08);
        var n = (int)(0.18 * Rate);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / Rate;
            var f = 620 + 480 * (t / 0.18);
            var v = (float)(Math.Sin(2 * Math.PI * f * t) * 0.05 * Math.Exp(-t * 14));
            b[i * 2] += v;
            b[i * 2 + 1] += v;
        }
        return b;
    }

    private static float[] Launch()
    {
        var b = Buffer(1.1);
        int[] notes = { 72, 76, 79, 84, 88 };
        for (var i = 0; i < notes.Length; i++)
            Bell(b, i * 0.065, Note(notes[i]), 0.9, 0.16, 5.5, -0.4 + i * 0.2);
        Bell(b, 0.36, Note(96), 0.7, 0.05, 6, 0.3, 0.2);
        return b;
    }

    private static float[] Startup()
    {
        var b = Buffer(3.2);
        Pad(b, 0, 3.1, new[] { Note(60), Note(64), Note(67), Note(71) }, 0.07);
        (double t, int n)[] melody = { (0.1, 76), (0.4, 79), (0.7, 86), (1.0, 84), (1.45, 83), (1.6, 79) };
        foreach (var (t, n) in melody) Bell(b, t, Note(n), 1.6, 0.13, 3.2, (n - 80) / 12.0);
        return b;
    }

    private static float[] Error()
    {
        var b = Buffer(0.4);
        Bell(b, 0, Note(64), 0.2, 0.14, 14, 0, 1.6);
        Bell(b, 0.13, Note(60), 0.25, 0.14, 12, 0, 1.6);
        return b;
    }

    private static float[] Glide(double from, double to, double seconds, double amp)
    {
        var b = Buffer(seconds + 0.05);
        var n = (int)(seconds * Rate);
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var f = from + (to - from) * (1 - Math.Pow(1 - t, 2));
            phase += 2 * Math.PI * f / Rate;
            var env = Math.Sin(Math.PI * t);
            var v = (float)(Math.Sin(phase) * amp * env);
            b[i * 2] += v;
            b[i * 2 + 1] += v;
        }
        return b;
    }

    /// <summary>
    /// Original 8-bar music-box loop (96 BPM) with a soft pad, bass, and light ticks.
    /// Rendered with an echo whose tail is wrapped so the loop is seamless.
    /// </summary>
    private static float[] Ambience()
    {
        const double bpm = 96;
        var beat = 60 / bpm;
        const int bars = 8;
        var loop = bars * 4 * beat;
        var total = loop + 3.0;
        var b = Buffer(total);

        int[][] chords =
        {
            new[] { 53, 57, 60, 64 }, // Fmaj7
            new[] { 52, 55, 59, 62 }, // Em7
            new[] { 50, 53, 57, 60 }, // Dm7
            new[] { 55, 60, 62, 65 }, // G7sus
            new[] { 53, 57, 60, 64 }, // Fmaj7
            new[] { 57, 60, 64, 67 }, // Am7
            new[] { 50, 53, 57, 60 }, // Dm7
            new[] { 48, 52, 55, 59 }, // Cmaj7
        };
        int[][] rhythms =
        {
            new[] { 1, 0, 1, 1, 0, 1, 0, 1 },
            new[] { 1, 0, 0, 1, 0, 1, 1, 0 },
        };

        for (var bar = 0; bar < bars; bar++)
        {
            var barStart = bar * 4 * beat;
            var chord = chords[bar];
            Pad(b, barStart, 4 * beat + 0.4, chord.Select(n => Note(n + 12)).ToArray(), 0.045);
            Bell(b, barStart, Note(chord[0] - 12), 1.2, 0.07, 3.5, 0, 0.2);
            Bell(b, barStart + 2 * beat, Note(chord[0] - 5), 1.0, 0.05, 3.5, 0, 0.2);

            var rhythm = rhythms[bar % 2];
            for (var step = 0; step < 8; step++)
            {
                var t = barStart + step * beat / 2;
                if (rhythm[step] == 1)
                {
                    var tone = chord[(step * 3 + bar) % chord.Length] + 24;
                    if (step == 7 && bar % 4 == 3) tone += 2;
                    Bell(b, t, Note(tone), 1.4, 0.075, 4.2, ((step % 3) - 1) * 0.35, 0.5);
                }
                if (step % 2 == 1) NoiseBurst(b, t, 0.03, 0.05, bar * 16 + step, 0.6);
            }
        }

        // Echo
        var delay = (int)(beat * 0.75 * Rate) * 2;
        for (var i = delay; i < b.Length; i++) b[i] += b[i - delay] * 0.28f;

        // Wrap the tail into the start for a seamless loop.
        var loopSamples = (int)(loop * Rate) * 2;
        var result = new float[loopSamples];
        Array.Copy(b, result, loopSamples);
        for (var i = loopSamples; i < b.Length; i++) result[(i - loopSamples) % loopSamples] += b[i];

        var peak = result.Max(Math.Abs);
        if (peak > 0.95f)
        {
            var scale = 0.95f / peak;
            for (var i = 0; i < result.Length; i++) result[i] *= scale;
        }
        return result;
    }
}
