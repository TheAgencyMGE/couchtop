namespace Couchtop.Core.Sports;

/// <summary>
/// A three-hole golf course for up to four players. Aim with the stick, the right club is picked for you
/// (B cycles clubs), hold A to swing back and let go at the power you want, or swing a Wii Remote.
/// The ball flies, bounces and rolls differently on fairway, rough, sand and green. Training: nearest the pin.
/// </summary>
public sealed class GolfSim : SportSim
{
    public const double CaptureRadius = 0.11;
    public const int MaxStrokes = 8;
    public const int TrainingShots = 5;

    public enum Terrain { Rough, Fairway, Green, Sand, Water, OutOfBounds, Tee }

    public enum GolfPhase { Aim, Backswing, Flight, Rolling, Settled, HoleOver }

    public sealed record Area(Terrain Terrain, double X, double Z, double RadiusX, double RadiusZ);

    public sealed record Hole(string Name, int Par, V3 Tee, V3 Pin, IReadOnlyList<Area> Areas, double MinX, double MaxX, double MaxZ, IReadOnlyList<V3> Route);

    public sealed record Club(string Name, double MaxCarry, double LaunchDegrees)
    {
        public bool IsPutter => LaunchDegrees <= 0;
    }

    public sealed class GolfBall
    {
        public V3 Position;
        public V3 Velocity;
        public bool Holed;
        public int Strokes;
        public bool Visible;
        internal V3 LastShotFrom;
        internal bool Penalized;
    }

    public static readonly IReadOnlyList<Club> Clubs = new[]
    {
        new Club("Driver", 200, 12),
        new Club("5 Iron", 160, 18),
        new Club("9 Iron", 115, 30),
        new Club("Wedge", 70, 44),
        new Club("Putter", 24, 0),
    };

    public static readonly IReadOnlyList<Hole> Course = new[]
    {
        new Hole("Breezy Meadow", 3, new V3(0, 0, 0), new V3(4, 0, 135),
            new[]
            {
                new Area(Terrain.Tee, 0, 0, 4, 6),
                new Area(Terrain.Fairway, 1, 72, 14, 58),
                new Area(Terrain.Green, 4, 135, 13, 11),
                new Area(Terrain.Sand, -11, 126, 6, 4),
                new Area(Terrain.Sand, 17, 141, 5, 4),
            },
            -45, 45, 175, new[] { new V3(4, 0, 135) }),
        new Hole("Lakeside Bend", 4, new V3(0, 0, 0), new V3(55, 0, 280),
            new[]
            {
                new Area(Terrain.Tee, 0, 0, 4, 6),
                new Area(Terrain.Fairway, 0, 110, 16, 72),
                new Area(Terrain.Fairway, 30, 215, 17, 46),
                new Area(Terrain.Green, 55, 280, 14, 12),
                new Area(Terrain.Sand, -15, 172, 8, 6),
                new Area(Terrain.Sand, 41, 267, 6, 4),
                new Area(Terrain.Water, -40, 232, 22, 30),
            },
            -75, 115, 320, new[] { new V3(0, 0, 150), new V3(30, 0, 218), new V3(55, 0, 280) }),
        new Hole("Island Hop", 3, new V3(0, 0, 0), new V3(-3, 0, 105),
            new[]
            {
                new Area(Terrain.Tee, 0, 0, 4, 6),
                new Area(Terrain.Water, 0, 58, 26, 20),
                new Area(Terrain.Fairway, -3, 92, 13, 8),
                new Area(Terrain.Green, -3, 105, 12, 10),
                new Area(Terrain.Sand, 11, 112, 5, 4),
                new Area(Terrain.Sand, -16, 100, 4, 5),
            },
            -40, 40, 140, new[] { new V3(-3, 0, 105) }),
    };

    private GolfBall[] _balls;
    private IReadOnlyList<PlayerCommand> _commands = Array.Empty<PlayerCommand>();
    private double _phaseTime;
    private int _current;
    private double _cpuYaw;
    private double _cpuPower;
    private int _shots;
    private int _trainingScore;
    private bool _biting;

    public GolfSim(SportSetup setup) : base(setup)
    {
        var players = IsTraining ? 1 : Math.Max(1, setup.Players);
        _balls = Enumerable.Range(0, players).Select(_ => new GolfBall()).ToArray();
        Scorer = new GolfScorer(players, Course.Select(h => h.Par).ToArray());
        HoleIndex = IsTraining ? 2 : 0;
        StartHole();
    }

    public GolfScorer Scorer { get; }
    public bool IsTraining => Setup.Mode == SportMode.Training;
    public int HoleIndex { get; private set; }
    public Hole CurrentHole => Course[HoleIndex];
    public IReadOnlyList<GolfBall> Balls => _balls;
    public GolfBall ActiveBall => _balls[_current];
    public override int ActivePlayer => _current;
    public GolfPhase Phase { get; private set; }
    public double PhaseTime => _phaseTime;
    public double AimYaw { get; private set; }
    public int ClubIndex { get; private set; }
    public Club CurrentClub => Clubs[ClubIndex];
    public double Meter { get; private set; }
    public int TrainingScore => _trainingScore;
    public int ShotsTaken => _shots;
    public double DistanceToPin => Flat(ActiveBall.Position, CurrentHole.Pin);

    public override string Scoreline => IsTraining
        ? $"Shot {Math.Min(_shots + 1, TrainingShots)}/{TrainingShots}  ·  Score {_trainingScore}"
        : $"Hole {HoleIndex + 1}/{Course.Count}  ·  Par {CurrentHole.Par}  ·  P{_current + 1}  ·  Stroke {ActiveBall.Strokes + 1}  ·  {DistanceToPin:0} m";

    private bool IsCpu => Setup.IsCpu(_current);

    private static double Flat(V3 a, V3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public static Terrain TerrainAt(Hole hole, double x, double z)
    {
        if (x < hole.MinX || x > hole.MaxX || z < -15 || z > hole.MaxZ) return Terrain.OutOfBounds;
        var best = Terrain.Rough;
        var bestRank = 0;
        foreach (var area in hole.Areas)
        {
            var dx = (x - area.X) / area.RadiusX;
            var dz = (z - area.Z) / area.RadiusZ;
            if (dx * dx + dz * dz > 1) continue;
            var rank = area.Terrain switch { Terrain.Water => 5, Terrain.Sand => 4, Terrain.Green => 3, Terrain.Tee => 2, Terrain.Fairway => 1, _ => 0 };
            if (rank > bestRank)
            {
                best = area.Terrain;
                bestRank = rank;
            }
        }
        return best;
    }

    public Terrain TerrainUnder(V3 p) => TerrainAt(CurrentHole, p.X, p.Z);

    protected override void Step(double dt, IReadOnlyList<PlayerCommand> commands)
    {
        _commands = commands;
        _phaseTime += dt;
        var cmd = CommandFor(commands, _current);
        switch (Phase)
        {
            case GolfPhase.Aim: StepAim(dt, cmd); break;
            case GolfPhase.Backswing:
                Meter = Math.Min(1.15, Meter + dt / 1.25);
                if (Meter >= 1.15 || (cmd.ActionReleased && _phaseTime > 0.15) || (cmd.ActionPressed && _phaseTime > 0.2)) Hit(Meter);
                break;
            case GolfPhase.Flight: StepFlight(dt); break;
            case GolfPhase.Rolling: StepRolling(dt); break;
            case GolfPhase.Settled:
                if (_phaseTime > 1.3) AfterShot();
                break;
            case GolfPhase.HoleOver:
                if (_phaseTime > 2.6) NextHole();
                break;
        }

        Prompt = IsCpu ? "" : Phase switch
        {
            GolfPhase.Aim => CurrentClub.IsPutter ? "Stick aims  ·  hold A (or swing) to putt" : "Stick aims  ·  B changes club  ·  hold A (or swing) to hit",
            GolfPhase.Backswing => "Let go at the power you want",
            _ => "",
        };
    }

    // ---------------------------------------------------------------- holes & turns

    private void StartHole()
    {
        foreach (var ball in _balls)
        {
            ball.Position = CurrentHole.Tee;
            ball.Velocity = default;
            ball.Holed = false;
            ball.Strokes = 0;
            ball.Visible = false;
        }
        _current = 0;
        if (!IsTraining) Emit(SportEventKind.Announce, -1, $"Hole {HoleIndex + 1}: {CurrentHole.Name}  ·  Par {CurrentHole.Par}");
        BeginTurn();
    }

    private void BeginTurn()
    {
        Phase = GolfPhase.Aim;
        _phaseTime = 0;
        Meter = 0;
        var ball = ActiveBall;
        ball.Visible = true;
        var target = AimTarget(ball.Position);
        AimYaw = Math.Atan2(target.X - ball.Position.X, target.Z - ball.Position.Z);
        ClubIndex = PickClub(ball.Position, target);

        if (IsCpu)
        {
            _cpuYaw = AimYaw + CpuError(0.045);
            var club = CurrentClub;
            var distance = Flat(ball.Position, target);
            var factor = club.IsPutter ? 1 : TerrainFactor(TerrainUnder(ball.Position), club);
            var wanted = club.IsPutter ? distance * 1.04 : distance * (target.Equals(CurrentHole.Pin) ? 0.93 : 1);
            _cpuPower = Math.Clamp(wanted / (club.MaxCarry * factor) + CpuError(club.IsPutter ? 0.05 : 0.07), 0.05, 1.0);
        }
    }

    private V3 AimTarget(V3 from)
    {
        var pin = CurrentHole.Pin;
        var toPin = Flat(from, pin);
        foreach (var point in CurrentHole.Route)
        {
            if (Flat(point, pin) < toPin - 5 && Flat(from, point) > 20) return point;
        }
        return pin;
    }

    private int PickClub(V3 from, V3 target)
    {
        if (TerrainUnder(from) == Terrain.Green) return Clubs.Count - 1;
        var distance = Flat(from, target);
        for (var i = Clubs.Count - 2; i >= 0; i--)
            if (Clubs[i].MaxCarry * TerrainFactor(TerrainUnder(from), Clubs[i]) >= distance * 1.02) return i;
        return 0;
    }

    private static double TerrainFactor(Terrain terrain, Club club) => terrain switch
    {
        Terrain.Rough => 0.85,
        Terrain.Sand => club.Name == "Wedge" ? 0.8 : 0.55,
        _ => 1,
    };

    private static double RollDecel(Terrain terrain) => terrain switch
    {
        Terrain.Green => 1.1,
        Terrain.Fairway or Terrain.Tee => 2.6,
        Terrain.Sand => 30,
        _ => 6.5,
    };

    private void StepAim(double dt, PlayerCommand cmd)
    {
        if (IsCpu)
        {
            AimYaw += Math.Clamp(_cpuYaw - AimYaw, -0.6 * dt, 0.6 * dt);
            if (_phaseTime > 1.5) Hit(_cpuPower);
            return;
        }

        AimYaw += cmd.MoveX * (CurrentClub.IsPutter ? 0.35 : 0.9) * dt;
        if (cmd.AltPressed) ClubIndex = (ClubIndex + 1) % Clubs.Count;
        if (cmd.Swing > 0) Hit(Math.Min(1.15, cmd.Swing * 1.1));
        else if (cmd.ActionPressed && _phaseTime > 0.15)
        {
            Phase = GolfPhase.Backswing;
            _phaseTime = 0;
            Meter = 0;
        }
    }

    private void Hit(double power)
    {
        var ball = ActiveBall;
        ball.Strokes++;
        ball.LastShotFrom = ball.Position;
        ball.Penalized = false;
        var club = CurrentClub;
        var terrain = TerrainUnder(ball.Position);
        var yaw = AimYaw;
        if (power > 1.0) yaw += (power - 1) * (Rng.NextDouble() * 2 - 1) * 1.2;

        if (club.IsPutter)
        {
            var distance = club.MaxCarry * Math.Min(power, 1.1);
            var speed = Math.Sqrt(2 * RollDecel(Terrain.Green) * distance);
            ball.Velocity = new V3(Math.Sin(yaw) * speed, 0, Math.Cos(yaw) * speed);
            _biting = false;
            Phase = GolfPhase.Rolling;
            Emit(SportEventKind.Putt, _current, null, (float)Math.Min(1, power));
        }
        else
        {
            var carry = Math.Max(1, club.MaxCarry * Math.Min(power, 1.15) * TerrainFactor(terrain, club));
            var theta = club.LaunchDegrees * Math.PI / 180;
            var speed = Math.Sqrt(carry * Ballistics.Gravity / Math.Sin(2 * theta));
            var horizontal = speed * Math.Cos(theta);
            ball.Velocity = new V3(Math.Sin(yaw) * horizontal, speed * Math.Sin(theta), Math.Cos(yaw) * horizontal);
            ball.Position.Y = 0.02;
            Phase = GolfPhase.Flight;
            Emit(SportEventKind.GolfSwing, _current, power > 1.02 ? "Overswing!" : null, (float)Math.Min(1, power));
        }
        Meter = power;
        _phaseTime = 0;
    }

    private void StepFlight(double dt)
    {
        var ball = ActiveBall;
        const int substeps = 4;
        var h = dt / substeps;
        for (var s = 0; s < substeps && Phase == GolfPhase.Flight; s++)
        {
            ball.Velocity.Y -= Ballistics.Gravity * h;
            ball.Position += ball.Velocity * h;
            if (ball.Position.Y > 0 || ball.Velocity.Y >= 0) continue;

            ball.Position.Y = 0;
            var terrain = TerrainUnder(ball.Position);
            if (terrain is Terrain.Water or Terrain.OutOfBounds)
            {
                Penalty(terrain);
                return;
            }
            if (Flat(ball.Position, CurrentHole.Pin) < CaptureRadius + 0.05 && ball.Velocity.Length < 25)
            {
                Holed();
                return;
            }

            Emit(SportEventKind.Land, _current, null, (float)Math.Min(1, -ball.Velocity.Y / 20));
            if (terrain == Terrain.Sand)
            {
                ball.Velocity = default;
                Emit(SportEventKind.Bunker, _current, "In the bunker");
                Settle();
                return;
            }
            // Steeper landings (lofted clubs) carry more backspin and stop sooner.
            var steep = Math.Clamp(-ball.Velocity.Y / Math.Max(1, ball.Velocity.Length), 0, 1);
            var (bounce, keep) = terrain switch
            {
                Terrain.Green => (0.18, 0.35),
                Terrain.Fairway or Terrain.Tee => (0.28, 0.45),
                _ => (0.12, 0.25),
            };
            keep *= 1 - 0.6 * steep;
            ball.Velocity = new V3(ball.Velocity.X * keep, -ball.Velocity.Y * bounce, ball.Velocity.Z * keep);
            if (ball.Velocity.Y < 1.5)
            {
                ball.Velocity.Y = 0;
                _biting = true;
                Phase = GolfPhase.Rolling;
                _phaseTime = 0;
            }
        }
    }

    private void StepRolling(double dt)
    {
        var ball = ActiveBall;
        const int substeps = 4;
        var h = dt / substeps;
        for (var s = 0; s < substeps && Phase == GolfPhase.Rolling; s++)
        {
            var terrain = TerrainUnder(ball.Position);
            if (terrain is Terrain.Water or Terrain.OutOfBounds)
            {
                Penalty(terrain);
                return;
            }
            var speed = Math.Sqrt(ball.Velocity.X * ball.Velocity.X + ball.Velocity.Z * ball.Velocity.Z);
            if (Flat(ball.Position, CurrentHole.Pin) < CaptureRadius && speed < 1.7)
            {
                Holed();
                return;
            }
            var decel = RollDecel(terrain) * (_biting && _phaseTime < 1.5 ? 2.5 : 1) * h;
            if (speed <= decel)
            {
                ball.Velocity = default;
                Settle();
                return;
            }
            var scale = (speed - decel) / speed;
            ball.Velocity = new V3(ball.Velocity.X * scale, 0, ball.Velocity.Z * scale);
            ball.Position += ball.Velocity * h;
        }
        if (_phaseTime > 25)
        {
            ball.Velocity = default;
            Settle();
        }
    }

    private void Penalty(Terrain terrain)
    {
        var ball = ActiveBall;
        ball.Penalized = true;
        if (!IsTraining) ball.Strokes++;
        ball.Position = ball.LastShotFrom;
        ball.Velocity = default;
        Emit(terrain == Terrain.Water ? SportEventKind.Splash : SportEventKind.Out, _current,
            IsTraining ? (terrain == Terrain.Water ? "Splash!" : "Out of bounds") : terrain == Terrain.Water ? "Splash! +1 stroke" : "Out of bounds  +1 stroke");
        Settle();
    }

    private void Holed()
    {
        var ball = ActiveBall;
        ball.Holed = true;
        ball.Position = CurrentHole.Pin;
        ball.Velocity = default;
        if (!IsTraining)
        {
            Scorer.Record(_current, HoleIndex, ball.Strokes);
            Emit(SportEventKind.Holed, _current, GolfScorer.Term(ball.Strokes, CurrentHole.Par));
        }
        else
        {
            Emit(SportEventKind.Holed, _current, "In the hole!");
        }
        Emit(SportEventKind.Cheer, _current);
        Settle();
    }

    private void Settle()
    {
        Phase = GolfPhase.Settled;
        _phaseTime = 0;
        var ball = ActiveBall;
        if (!IsTraining && !ball.Holed && ball.Strokes >= MaxStrokes)
        {
            ball.Holed = true;
            Scorer.Record(_current, HoleIndex, MaxStrokes);
            Emit(SportEventKind.Announce, _current, "Picked up");
        }
    }

    private void AfterShot()
    {
        if (IsTraining)
        {
            var ball = ActiveBall;
            var points = ball.Holed ? 15 : ball.Penalized ? 0 : Math.Max(0, (int)Math.Round(10 - Flat(ball.Position, CurrentHole.Pin) * 0.8));
            if (!ball.Penalized && !ball.Holed) Emit(SportEventKind.Target, 0, $"{Flat(ball.Position, CurrentHole.Pin):0.0} m  ·  +{points}", points / 10f);
            _trainingScore += points;
            _shots++;
            if (_shots >= TrainingShots)
            {
                Finish();
                return;
            }
            ball.Position = CurrentHole.Tee;
            ball.Holed = false;
            ball.Strokes = 0;
            BeginTurn();
            return;
        }

        if (_balls.All(b => b.Holed))
        {
            Phase = GolfPhase.HoleOver;
            _phaseTime = 0;
            return;
        }

        var next = -1;
        var farthest = -1.0;
        for (var i = 0; i < _balls.Length; i++)
        {
            if (_balls[i].Holed) continue;
            var d = Flat(_balls[i].Position, CurrentHole.Pin) + (_balls[i].Strokes == 0 ? 10000 - i : 0);
            if (d > farthest)
            {
                farthest = d;
                next = i;
            }
        }
        _current = next;
        BeginTurn();
    }

    private void NextHole()
    {
        if (HoleIndex + 1 >= Course.Count)
        {
            Finish();
            return;
        }
        HoleIndex++;
        StartHole();
    }

    private void Finish()
    {
        if (IsFinished) return;
        if (IsTraining)
        {
            Result = new SportResult(SportKind.Golf, SportMode.Training, new[] { _trainingScore }, null,
                $"{_trainingScore} points", "Nearest the Pin", _trainingScore);
        }
        else
        {
            var totals = Enumerable.Range(0, _balls.Length).Select(Scorer.Total).ToArray();
            var best = totals.Min();
            int? winner = totals.Length > 1 && totals.Count(t => t == best) == 1 ? Array.IndexOf(totals, best) : null;
            var second = totals.Length > 1 ? totals.OrderBy(t => t).ElementAt(1) : best;
            var headline = totals.Length == 1 ? $"{totals[0]} strokes ({GolfScorer.ToParText(Scorer.ToPar(0))})" : winner is null ? "It's a tie!" : $"P{winner + 1} wins!";
            Result = new SportResult(SportKind.Golf, SportMode.Match, totals, winner, headline,
                string.Join("   ", totals.Select((t, i) => $"P{i + 1}: {t} ({GolfScorer.ToParText(Scorer.ToPar(i))})")), Margin: second - best);
        }
        Emit(SportEventKind.Finish);
    }
}
