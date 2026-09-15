namespace Couchtop.Core.Sports;

/// <summary>
/// Three-inning baseball. Batting is pure timing (swing as the ball reaches the plate); pitching is picking a
/// pitch with the stick and throwing when the accuracy meter is centered. Training: a ten-pitch home run derby.
/// </summary>
public sealed class BaseballSim : SportSim
{
    public const double MoundDistance = 18.44;
    public const int Innings = 3;
    public const int DerbyPitches = 10;
    private const double SwingToContact = 0.1;

    public enum BaseballPhase { Ready, Windup, Pitch, Flight, Result }

    public enum PitchKind { Fastball, Curveball, Changeup }

    public enum Outcome { None, Strike, Foul, GroundOut, FlyOut, PopOut, Single, Double, Triple, HomeRun }

    private readonly bool[] _bases = new bool[3];
    private readonly int[] _runs = new int[2];
    private IReadOnlyList<PlayerCommand> _commands = Array.Empty<PlayerCommand>();
    private double _phaseTime;
    private double _pitchQuality;
    private double _swingStart = -1;
    private float _swingPower;
    private double _cpuSwingAt = double.NaN;
    private float _cpuPower;
    private V3 _flightStart;
    private V3 _flightVelocity;
    private Outcome _pendingOutcome;
    private int _derbyPitches;
    private int _homeRuns;

    public BaseballSim(SportSetup setup) : base(setup)
    {
        Phase = BaseballPhase.Ready;
    }

    public BaseballPhase Phase { get; private set; }
    public double PhaseTime => _phaseTime;
    public bool IsDerby => Setup.Mode == SportMode.Training;
    public int Inning { get; private set; } = 1;
    public bool TopHalf { get; private set; } = true;
    public int Outs { get; private set; }
    public int Strikes { get; private set; }
    public IReadOnlyList<bool> Bases => _bases;
    public IReadOnlyList<int> Runs => _runs;
    public int HomeRuns => _homeRuns;
    public int PitchesThrown => _derbyPitches;
    public PitchKind Pitch { get; private set; }
    public double PitchDuration { get; private set; } = 0.6;
    public double PitchProgress { get; private set; }
    public double PitchMeter { get; private set; }
    public V3 Ball { get; private set; }
    public V3 LandingPoint { get; private set; }
    public double FlightTime { get; private set; }
    public Outcome LastOutcome { get; private set; }

    /// <summary>Seconds since the batter's swing started, or -1.</summary>
    public double SwingTime { get; private set; } = -1;

    public int Batter => IsDerby ? 0 : TopHalf ? 0 : 1;
    public int Pitcher => IsDerby ? 1 : 1 - Batter;
    private bool IsHuman(int player) => !(IsDerby && player == 1) && !Setup.IsCpu(player);

    public override string Scoreline => IsDerby
        ? $"Pitch {Math.Min(_derbyPitches + 1, DerbyPitches)}/{DerbyPitches}  ·  Home runs {_homeRuns}"
        : $"{(TopHalf ? "Top" : "Bottom")} {Inning}  ·  P1 {_runs[0]} - {_runs[1]} P2  ·  Outs {Outs}  ·  Strikes {Strikes}";

    protected override void Step(double dt, IReadOnlyList<PlayerCommand> commands)
    {
        _commands = commands;
        _phaseTime += dt;
        if (SwingTime >= 0)
        {
            SwingTime += dt;
            if (SwingTime > 0.6) SwingTime = -1;
        }

        switch (Phase)
        {
            case BaseballPhase.Ready: StepReady(); break;
            case BaseballPhase.Windup:
                Ball = new V3(0.25, 1.6, MoundDistance - 0.3);
                if (_phaseTime >= 0.75) BeginPitch();
                break;
            case BaseballPhase.Pitch: StepPitch(); break;
            case BaseballPhase.Flight: StepFlight(); break;
            case BaseballPhase.Result: StepResult(); break;
        }

        Prompt = Phase switch
        {
            BaseballPhase.Ready when IsHuman(Pitcher) => "Stick picks the pitch  ·  press A when the meter is centered",
            BaseballPhase.Ready or BaseballPhase.Windup or BaseballPhase.Pitch when IsHuman(Batter) => "Swing when the ball reaches the plate",
            _ => "",
        };
    }

    private void StepReady()
    {
        Ball = new V3(0.25, 1.4, MoundDistance - 0.2);
        SwingTime = -1;
        _swingStart = -1;
        if (IsHuman(Pitcher))
        {
            PitchMeter = Math.Sin(_phaseTime * 3.2);
            var c = CommandFor(_commands, Pitcher);
            Pitch = c.MoveX < -0.4 ? PitchKind.Curveball : c.MoveX > 0.4 ? PitchKind.Changeup : PitchKind.Fastball;
            if (_phaseTime > 0.5 && c.SwingNow)
            {
                _pitchQuality = 1 - Math.Abs(PitchMeter);
                BeginWindup();
            }
        }
        else if (_phaseTime > 1.4)
        {
            var roll = Rng.NextDouble();
            Pitch = IsDerby
                ? (roll < 0.75 ? PitchKind.Fastball : PitchKind.Changeup)
                : roll < 0.5 ? PitchKind.Fastball : roll < 0.78 ? PitchKind.Curveball : PitchKind.Changeup;
            _pitchQuality = IsDerby ? 0.35 : Math.Clamp(0.45 + Setup.Difficulty * 0.13 + CpuError(0.2), 0, 1);
            BeginWindup();
        }
    }

    private void BeginWindup()
    {
        Phase = BaseballPhase.Windup;
        _phaseTime = 0;
    }

    private void BeginPitch()
    {
        Phase = BaseballPhase.Pitch;
        _phaseTime = 0;
        PitchProgress = 0;
        SwingTime = -1;
        _swingStart = -1;
        PitchDuration = Pitch switch
        {
            PitchKind.Fastball => 0.56,
            PitchKind.Curveball => 0.7,
            _ => 0.84,
        };
        PitchDuration *= 1.08 - 0.12 * _pitchQuality;
        Emit(SportEventKind.Pitch, Pitcher, Pitch.ToString());

        _cpuSwingAt = double.NaN;
        if (!IsHuman(Batter))
        {
            var skill = 0.45 + Setup.Difficulty * 0.13 - (_pitchQuality - 0.5) * 0.3;
            if (Rng.NextDouble() > 0.15)
            {
                // Triangular timing error (sum of two uniforms), plus a chance of being fooled by off-speed pitches.
                var spread = 0.05 + (1 - skill) * 0.13;
                var timing = (Range(-spread, spread) + Range(-spread, spread)) * 0.75;
                var fooled = 0.08 + (Pitch == PitchKind.Fastball ? 0 : 0.1) + _pitchQuality * 0.08 - Setup.Difficulty * 0.02;
                if (Rng.NextDouble() < fooled) timing += (Rng.Next(2) == 0 ? -1 : 1) * Range(0.1, 0.18);
                _cpuSwingAt = PitchDuration - SwingToContact + timing;
                _cpuPower = (float)Range(0.3, 0.7 + Setup.Difficulty * 0.06);
            }
        }
    }

    /// <summary>Where the pitched ball is at a given fraction of its flight (0 = hand, 1 = plate).</summary>
    public V3 PitchBallPosition(double progress)
    {
        var p = Math.Clamp(progress, 0, 1.25);
        var arc = Math.Sin(Math.PI * Math.Min(p, 1));
        var x = 0.25 * (1 - p);
        if (Pitch == PitchKind.Curveball) x += arc * 0.45 * p;
        var y = 1.9 + (0.85 - 1.9) * p + arc * (Pitch == PitchKind.Changeup ? 0.45 : 0.22);
        return new V3(x, y, MoundDistance * (1 - p));
    }

    private void StepPitch()
    {
        PitchProgress = _phaseTime / PitchDuration;
        Ball = PitchBallPosition(PitchProgress);

        if (_swingStart < 0)
        {
            if (IsHuman(Batter))
            {
                var c = CommandFor(_commands, Batter);
                // A button swing is a solid swing: perfect timing is always a home run.
                if (c.SwingNow) StartSwing(c.Swing > 0 ? Math.Max(0.4f, c.Swing) : 0.75f);
            }
            else if (!double.IsNaN(_cpuSwingAt) && _phaseTime >= _cpuSwingAt)
            {
                StartSwing(_cpuPower);
            }
        }

        if (_phaseTime >= PitchDuration + 0.16) ResolvePitch();
    }

    private void StartSwing(float power)
    {
        _swingStart = _phaseTime;
        _swingPower = power;
        SwingTime = 0;
        Emit(SportEventKind.Swing, Batter);
    }

    private void ResolvePitch()
    {
        if (_swingStart < 0)
        {
            Emit(SportEventKind.Catch, Pitcher);
            FinishPlay(Outcome.Strike, "Strike! (looking)");
            return;
        }

        var delta = _swingStart + SwingToContact - PitchDuration;
        var miss = Math.Abs(delta);
        if (miss > 0.11)
        {
            Emit(SportEventKind.Catch, Pitcher);
            FinishPlay(Outcome.Strike, "Swing and a miss!");
            return;
        }

        var quality = miss <= 0.025 ? 2 : miss <= 0.06 ? 1 : 0;
        var roll = Rng.NextDouble();
        var power = _swingPower;
        var outcome = quality switch
        {
            2 => power > 0.7 ? Outcome.HomeRun : roll < 0.3 ? Outcome.Triple : roll < 0.7 ? Outcome.Double : Outcome.Single,
            1 => roll < (power > 0.85 ? 0.08 : 0.03) ? Outcome.HomeRun : roll < 0.41 ? Outcome.Single : roll < 0.55 ? Outcome.Double : roll < 0.84 ? Outcome.FlyOut : Outcome.GroundOut,
            _ => roll < 0.4 ? Outcome.Foul : roll < 0.7 ? Outcome.GroundOut : roll < 0.92 ? Outcome.PopOut : Outcome.Single,
        };

        var angle = Math.Clamp(delta * 480, -44, 44);
        if (outcome == Outcome.Foul) angle = (delta >= 0 ? 1 : -1) * Range(52, 75);
        var radians = angle * Math.PI / 180;
        var (distance, time) = outcome switch
        {
            Outcome.HomeRun => (Range(112, 140), 3.6),
            Outcome.Triple => (Range(88, 104), 2.6),
            Outcome.Double => (Range(66, 86), 2.2),
            Outcome.Single => (Range(32, 55), 1.3),
            Outcome.FlyOut => (Range(55, 82), 3.2),
            Outcome.PopOut => (Range(14, 32), 3.0),
            Outcome.GroundOut => (Range(18, 34), 0.9),
            _ => (Range(18, 55), 2.0),
        };

        LandingPoint = new V3(Math.Sin(radians) * distance, 0, Math.Cos(radians) * distance);
        _flightStart = new V3(0, 1.0, 0.3);
        _flightVelocity = Ballistics.LaunchVelocity(_flightStart, LandingPoint, time);
        FlightTime = time;
        _pendingOutcome = outcome;
        Phase = BaseballPhase.Flight;
        _phaseTime = 0;
        Emit(SportEventKind.Contact, Batter, quality == 2 ? "Crushed it!" : null, quality / 2f);
    }

    private void StepFlight()
    {
        var t = _phaseTime;
        if (t <= FlightTime)
        {
            Ball = new V3(
                _flightStart.X + _flightVelocity.X * t,
                Math.Max(0.04, _flightStart.Y + _flightVelocity.Y * t - 0.5 * Ballistics.Gravity * t * t),
                _flightStart.Z + _flightVelocity.Z * t);
        }
        if (t < FlightTime + 0.6) return;
        FinishPlay(_pendingOutcome, null);
    }

    private void FinishPlay(Outcome outcome, string? text)
    {
        LastOutcome = outcome;
        Phase = BaseballPhase.Result;
        _phaseTime = 0;
        var batter = Batter;

        if (IsDerby)
        {
            _derbyPitches++;
            if (outcome == Outcome.HomeRun)
            {
                _homeRuns++;
                Emit(SportEventKind.HomeRun, 0, "Home run!");
                Emit(SportEventKind.Cheer, 0);
            }
            else
            {
                Emit(SportEventKind.Announce, 0, text ?? Describe(outcome));
            }
            return;
        }

        switch (outcome)
        {
            case Outcome.Strike:
                Strikes++;
                if (Strikes >= 3)
                {
                    Emit(SportEventKind.StrikeOut, batter, "Strike three, you're out!");
                    RecordOut();
                }
                else
                {
                    Emit(SportEventKind.Strike, batter, text ?? $"Strike {Strikes}");
                }
                break;
            case Outcome.Foul:
                if (Strikes < 2) Strikes++;
                Emit(SportEventKind.Foul, batter, "Foul ball");
                break;
            case Outcome.GroundOut or Outcome.FlyOut or Outcome.PopOut:
                Emit(SportEventKind.Catch, Pitcher, Describe(outcome));
                RecordOut();
                break;
            default:
                var gained = outcome switch { Outcome.Single => 1, Outcome.Double => 2, Outcome.Triple => 3, _ => 4 };
                var runs = BaseRunners.Advance(_bases, gained);
                _runs[batter] += runs;
                Strikes = 0;
                Emit(outcome switch
                {
                    Outcome.Single => SportEventKind.Single,
                    Outcome.Double => SportEventKind.Double,
                    Outcome.Triple => SportEventKind.Triple,
                    _ => SportEventKind.HomeRun,
                }, batter, Describe(outcome));
                if (runs > 0)
                {
                    Emit(SportEventKind.Run, batter, runs == 1 ? "A run scores!" : $"{runs} runs score!");
                    Emit(SportEventKind.Cheer, batter);
                }
                if (!TopHalf && Inning >= Innings && _runs[1] > _runs[0]) Finish();
                break;
        }
    }

    private static string Describe(Outcome outcome) => outcome switch
    {
        Outcome.Single => "Base hit!",
        Outcome.Double => "Double!",
        Outcome.Triple => "Triple!",
        Outcome.HomeRun => "Home run!",
        Outcome.GroundOut => "Grounded out",
        Outcome.FlyOut => "Caught! Fly out",
        Outcome.PopOut => "Popped up, out",
        Outcome.Foul => "Foul ball",
        _ => "Strike",
    };

    private void RecordOut()
    {
        Outs++;
        Strikes = 0;
        if (Outs >= 3) EndHalfInning();
    }

    private void EndHalfInning()
    {
        Outs = 0;
        Strikes = 0;
        Array.Clear(_bases);
        if (TopHalf)
        {
            if (Inning >= Innings && _runs[1] > _runs[0])
            {
                Finish();
                return;
            }
            TopHalf = false;
            Emit(SportEventKind.Announce, -1, "Switch sides");
        }
        else
        {
            if (Inning >= Innings && _runs[0] != _runs[1] || Inning >= Innings + 1)
            {
                Finish();
                return;
            }
            Inning++;
            TopHalf = true;
            Emit(SportEventKind.Announce, -1, Inning > Innings ? "Extra inning!" : $"Inning {Inning}");
        }
    }

    private void StepResult()
    {
        if (_phaseTime < 1.3 || IsFinished) return;
        if (IsDerby && _derbyPitches >= DerbyPitches)
        {
            Finish();
            return;
        }
        Phase = BaseballPhase.Ready;
        _phaseTime = 0;
    }

    private void Finish()
    {
        if (IsFinished) return;
        if (IsDerby)
        {
            Result = new SportResult(SportKind.Baseball, SportMode.Training, new[] { _homeRuns }, null,
                _homeRuns == 1 ? "1 home run" : $"{_homeRuns} home runs", "Home Run Derby", _homeRuns);
        }
        else
        {
            int? winner = _runs[0] > _runs[1] ? 0 : _runs[1] > _runs[0] ? 1 : null;
            Result = new SportResult(SportKind.Baseball, SportMode.Match, _runs.ToArray(), winner,
                winner is null ? "It's a tie!" : $"P{winner + 1} wins!", $"Final score {_runs[0]} - {_runs[1]}", Margin: Math.Abs(_runs[0] - _runs[1]));
        }
        Emit(SportEventKind.Finish);
    }
}
