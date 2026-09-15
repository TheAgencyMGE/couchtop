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

    // Couchtop Sports
    SportSwing,
    SportHit,
    SportPowerHit,
    SportBounce,
    SportNet,
    BatCrack,
    MittPop,
    BowlRoll,
    PinCrash,
    PinTap,
    GolfSwing,
    GolfClick,
    Putt,
    CupRattle,
    Splash,
    SandThud,
    Punch,
    PunchHeavy,
    Block,
    BoxBell,
    CrowdCheer,
    CrowdGroan,
    Applause,
    CountBeep,
    CountGo,
    Victory,
    Defeat,
    Medal,
    Point,
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
        AddSportsSounds(bank);
        if (includeAmbience) bank.Ambience = Ambience();
        return bank;
    }

    // ---------------------------------------------------------------- Couchtop Sports

    private static void AddSportsSounds(SoundBank bank)
    {
        bank.Effects[SoundEffect.SportSwing] = Whoosh(0.22, 0.28, 11);
        bank.Effects[SoundEffect.SportHit] = Build(0.16, b =>
        {
            Thump(b, 0, 760, 430, 0.07, 0.5);
            Noise(b, 0, 0.04, 0.22, 3, 0.6, decay: 60);
        });
        bank.Effects[SoundEffect.SportPowerHit] = Build(0.3, b =>
        {
            Thump(b, 0, 560, 250, 0.11, 0.65);
            Noise(b, 0, 0.06, 0.35, 5, 0.75, decay: 40);
            Bell(b, 0.005, 1260, 0.2, 0.07, 22, 0, 0.6);
        });
        bank.Effects[SoundEffect.SportBounce] = Build(0.1, b => Thump(b, 0, 270, 140, 0.07, 0.35));
        bank.Effects[SoundEffect.SportNet] = Build(0.4, b =>
        {
            Noise(b, 0, 0.3, 0.3, 9, 0.12, decay: 9);
            Thump(b, 0, 150, 90, 0.12, 0.25);
        });
        bank.Effects[SoundEffect.BatCrack] = Build(0.35, b =>
        {
            Noise(b, 0, 0.06, 0.6, 13, 0.9, decay: 55);
            Thump(b, 0, 950, 480, 0.06, 0.45);
            Bell(b, 0.002, 2350, 0.25, 0.1, 30, 0, 1.4);
        });
        bank.Effects[SoundEffect.MittPop] = Build(0.2, b =>
        {
            Thump(b, 0, 190, 105, 0.1, 0.6);
            Noise(b, 0, 0.05, 0.3, 15, 0.35, decay: 50);
        });
        bank.Effects[SoundEffect.BowlRoll] = Build(1.3, b =>
        {
            Noise(b, 0, 1.25, 0.45, 17, 0.025, attack: 0.08, decay: 2.2);
            Thump(b, 0, 72, 60, 1.2, 0.18, decay: 2.5);
        });
        bank.Effects[SoundEffect.PinCrash] = Build(1.0, b =>
        {
            var rng = new Random(19);
            for (var i = 0; i < 16; i++)
                Bell(b, rng.NextDouble() * 0.45, 1300 + rng.NextDouble() * 1400, 0.12, 0.13, 45, rng.NextDouble() * 1.4 - 0.7, 1.5);
            Noise(b, 0, 0.5, 0.3, 21, 0.25, decay: 6);
            Thump(b, 0, 240, 120, 0.12, 0.35);
        });
        bank.Effects[SoundEffect.PinTap] = Build(0.3, b =>
        {
            Bell(b, 0, 1900, 0.1, 0.14, 50, -0.2, 1.4);
            Bell(b, 0.07, 1600, 0.1, 0.1, 50, 0.2, 1.4);
        });
        bank.Effects[SoundEffect.GolfSwing] = Whoosh(0.32, 0.32, 23);
        bank.Effects[SoundEffect.GolfClick] = Build(0.3, b =>
        {
            Bell(b, 0, 3100, 0.2, 0.2, 55, 0, 0.5);
            Thump(b, 0, 1300, 650, 0.035, 0.35);
            Noise(b, 0, 0.03, 0.2, 25, 0.9, decay: 80);
        });
        bank.Effects[SoundEffect.Putt] = Build(0.2, b =>
        {
            Thump(b, 0, 520, 380, 0.05, 0.3);
            Bell(b, 0, 1850, 0.1, 0.06, 45, 0, 0.3);
        });
        bank.Effects[SoundEffect.CupRattle] = Build(0.8, b =>
        {
            for (var i = 0; i < 5; i++) Bell(b, i * 0.055, 1500 - i * 70, 0.12, 0.12 - i * 0.015, 40, (i % 2) * 0.4 - 0.2, 1.2);
            Bell(b, 0.32, Note(84), 0.45, 0.12, 8);
            Bell(b, 0.4, Note(91), 0.45, 0.1, 8);
        });
        bank.Effects[SoundEffect.Splash] = Build(0.9, b =>
        {
            Noise(b, 0, 0.7, 0.5, 27, 0.45, attack: 0.01, decay: 5);
            Noise(b, 0.12, 0.5, 0.25, 29, 0.15, attack: 0.02, decay: 6);
            Thump(b, 0, 180, 70, 0.2, 0.3);
        });
        bank.Effects[SoundEffect.SandThud] = Build(0.3, b =>
        {
            Noise(b, 0, 0.2, 0.4, 31, 0.1, decay: 14);
            Thump(b, 0, 120, 70, 0.12, 0.3);
        });
        bank.Effects[SoundEffect.Punch] = Build(0.25, b =>
        {
            Thump(b, 0, 170, 90, 0.11, 0.7);
            Noise(b, 0, 0.05, 0.35, 33, 0.5, decay: 45);
        });
        bank.Effects[SoundEffect.PunchHeavy] = Build(0.4, b =>
        {
            Thump(b, 0, 130, 55, 0.18, 0.85);
            Noise(b, 0, 0.08, 0.45, 35, 0.6, decay: 30);
            Bell(b, 0.01, 420, 0.25, 0.08, 18, 0, 0.8);
        });
        bank.Effects[SoundEffect.Block] = Build(0.2, b =>
        {
            Thump(b, 0, 230, 180, 0.07, 0.4);
            Noise(b, 0, 0.04, 0.2, 37, 0.25, decay: 50);
        });
        bank.Effects[SoundEffect.BoxBell] = Build(2.2, b =>
        {
            Bell(b, 0, 1318, 2.1, 0.32, 2.2, 0, 1.8);
            Bell(b, 0, 1318 * 2.76, 1.2, 0.07, 3, 0, 0.5);
            Bell(b, 0.25, 1318, 1.8, 0.2, 2.4, 0, 1.8);
        });
        bank.Effects[SoundEffect.CrowdCheer] = Crowd(2.0, rising: true, seed: 41);
        bank.Effects[SoundEffect.CrowdGroan] = Build(1.4, b =>
        {
            Noise(b, 0, 1.3, 0.25, 43, 0.04, attack: 0.1, decay: 2.2);
            foreach (var detune in new[] { 0.0, 7, -9 })
            {
                var glide = Glide(300 + detune, 170 + detune, 1.1, 0.035);
                for (var i = 0; i < glide.Length && i < b.Length; i++) b[i] += glide[i];
            }
        });
        bank.Effects[SoundEffect.Applause] = Build(1.8, b =>
        {
            var rng = new Random(47);
            for (var i = 0; i < 700; i++)
            {
                var t = rng.NextDouble() * 1.6;
                var envelope = Math.Min(1, t / 0.15) * Math.Min(1, (1.6 - t) / 0.6);
                Noise(b, t, 0.012, 0.16 * envelope, 1000 + i, 0.6 + rng.NextDouble() * 0.3, decay: 200);
            }
        });
        bank.Effects[SoundEffect.CountBeep] = Build(0.2, b => Bell(b, 0, 880, 0.18, 0.22, 18, 0, 0.3));
        bank.Effects[SoundEffect.CountGo] = Build(0.6, b =>
        {
            Bell(b, 0, 1320, 0.5, 0.25, 7, 0, 0.4);
            Bell(b, 0, 1760, 0.5, 0.12, 7, 0, 0.3);
        });
        bank.Effects[SoundEffect.Victory] = Build(1.6, b =>
        {
            int[] notes = { 72, 76, 79, 84 };
            for (var i = 0; i < notes.Length; i++) Bell(b, i * 0.11, Note(notes[i]), 0.9, 0.16, 5, -0.3 + i * 0.2);
            foreach (var n in new[] { 72, 76, 79, 88 }) Bell(b, 0.5, Note(n), 1.1, 0.1, 3.2, (n - 80) / 20.0);
            Bell(b, 0.7, Note(96), 0.8, 0.05, 5, 0.3, 0.3);
        });
        bank.Effects[SoundEffect.Defeat] = Build(1.3, b =>
        {
            int[] notes = { 67, 64, 60, 59 };
            for (var i = 0; i < notes.Length; i++) Bell(b, i * 0.16, Note(notes[i]), 0.8, 0.12, 5, 0, 0.6);
        });
        bank.Effects[SoundEffect.Medal] = Build(1.5, b =>
        {
            int[] notes = { 88, 91, 96, 100, 103 };
            for (var i = 0; i < notes.Length; i++) Bell(b, i * 0.07, Note(notes[i]), 0.8, 0.1, 6, -0.4 + i * 0.2, 0.4);
            Pad(b, 0.2, 1.2, new[] { Note(72), Note(76), Note(79) }, 0.05);
        });
        bank.Effects[SoundEffect.Point] = Build(0.4, b =>
        {
            Bell(b, 0, Note(84), 0.35, 0.13, 10);
            Bell(b, 0.05, Note(88), 0.35, 0.11, 10);
        });
    }

    private static float[] Build(double seconds, Action<float[]> draw)
    {
        var b = Buffer(seconds);
        draw(b);
        return b;
    }

    /// <summary>Pitched thump that glides from one frequency to another (impacts, bounces, punches).</summary>
    private static void Thump(float[] buf, double start, double fromHz, double toHz, double duration, double amp, double decay = 0)
    {
        var s = (int)(start * Rate);
        var n = (int)(duration * Rate);
        if (decay <= 0) decay = 5.5 / duration;
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var idx = (s + i) * 2;
            if (idx + 1 >= buf.Length) break;
            var t = (double)i / Rate;
            var f = fromHz * Math.Pow(toHz / fromHz, t / duration);
            phase += 2 * Math.PI * f / Rate;
            var env = Math.Min(1, t / 0.002) * Math.Exp(-t * decay);
            var v = (float)(Math.Sin(phase) * amp * env);
            buf[idx] += v;
            buf[idx + 1] += v;
        }
    }

    /// <summary>Low-passed noise with a quick attack and exponential decay.</summary>
    private static void Noise(float[] buf, double start, double duration, double amp, int seed, double lowpass, double attack = 0.002, double decay = 20)
    {
        var rng = new Random(seed);
        var s = (int)(start * Rate);
        var n = (int)(duration * Rate);
        double y = 0;
        for (var i = 0; i < n; i++)
        {
            var idx = (s + i) * 2;
            if (idx + 1 >= buf.Length) break;
            var t = (double)i / Rate;
            var env = Math.Min(1, t / attack) * Math.Exp(-t * decay) * Math.Min(1, (duration - t) / 0.01);
            y += lowpass * ((rng.NextDouble() * 2 - 1) - y);
            var v = (float)(y * amp * env);
            buf[idx] += v;
            buf[idx + 1] += v;
        }
    }

    /// <summary>Swing whoosh: noise whose brightness rises and falls, panned across.</summary>
    private static float[] Whoosh(double seconds, double amp, int seed)
    {
        var b = Buffer(seconds + 0.02);
        var rng = new Random(seed);
        var n = (int)(seconds * Rate);
        double y = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var shape = Math.Sin(Math.PI * t);
            var cutoff = 0.02 + 0.3 * shape * shape;
            y += cutoff * ((rng.NextDouble() * 2 - 1) - y);
            var v = y * amp * shape * 2.2;
            var pan = t * 2 - 1;
            b[i * 2] += (float)(v * (1 - pan) * 0.6);
            b[i * 2 + 1] += (float)(v * (1 + pan) * 0.6);
        }
        return b;
    }

    /// <summary>A stadium crowd: several bands of noise with slow, independent swells, plus a few "whoo" glides.</summary>
    private static float[] Crowd(double seconds, bool rising, int seed)
    {
        var b = Buffer(seconds);
        var rng = new Random(seed);
        var n = (int)(seconds * Rate);
        for (var layer = 0; layer < 6; layer++)
        {
            var cutoff = 0.04 + layer * 0.03;
            var rate = 2 + rng.NextDouble() * 5;
            var offset = rng.NextDouble() * 6;
            var pan = rng.NextDouble() * 1.4 - 0.7;
            double y = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var env = (rising ? Math.Min(1, t / 0.18) : 1) * Math.Min(1, (seconds - t) / (seconds * 0.6));
                var swell = 0.75 + 0.25 * Math.Sin(2 * Math.PI * rate * t + offset);
                y += cutoff * ((rng.NextDouble() * 2 - 1) - y);
                var v = y * 0.18 * env * swell;
                b[i * 2] += (float)(v * (1 - pan));
                b[i * 2 + 1] += (float)(v * (1 + pan));
            }
        }
        for (var k = 0; k < 4; k++)
        {
            var glide = Glide(320 + rng.Next(120), 620 + rng.Next(160), 0.5, 0.02);
            var start = (int)((0.1 + rng.NextDouble() * 0.6) * Rate) * 2;
            for (var i = 0; i < glide.Length && start + i < b.Length; i++) b[start + i] += glide[i];
        }
        return b;
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
