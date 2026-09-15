namespace Couchtop.Core.Sports;

/// <summary>
/// Singles tennis. Players run to the ball automatically; you only time the swing (early swings pull the ball
/// across the court, late swings push it the other way, the stick fine-tunes the aim). Short match: first to
/// two games. Training: return balls from a machine onto targets.
/// </summary>
public sealed class TennisSim : SportSim
{
    public const double HalfLength = 11.885;
    public const double HalfWidth = 4.115;
    public const double ServiceLine = 6.40;
    public const double NetHeight = 0.914;
    public const double BallRadius = 0.04;
    public const double IdealSwingTime = 0.13;
    public const int TrainingBalls = 15;
    private const double ReachRadius = 1.4;
    private const double TossSpeed = 4.8;

    public enum TennisPhase { Serve, Toss, Rally, PointOver }

    public sealed class Player
    {
        public V3 Position;

        /// <summary>Seconds since the current swing started, or -1 when not swinging.</summary>
        public double SwingTime = -1;

        /// <summary>+1 forehand side, -1 backhand side.</summary>
        public int SwingSide = 1;

        public double Speed;
        internal float SwingPower;
        internal bool HasHit;
        internal double CpuTimingError;
    }

    /// <summary>Training targets on the far court: center (x, z) and radius.</summary>
    public static readonly (double X, double Z, double Radius)[] Targets = { (-2.6, 9.3, 1.3), (2.6, 9.3, 1.3), (0, 4.4, 1.1) };

    private readonly Player[] _players = { new(), new() };
    private IReadOnlyList<PlayerCommand> _commands = Array.Empty<PlayerCommand>();
    private V3 _ball;
    private V3 _velocity;
    private int _lastHitter = -1;
    private int _bounces;
    private bool _serving;
    private int _serveBoxSign = 1;
    private int _faults;
    private double _phaseTime;
    private bool _secondServe;
    private double _cpuTossHeight;
    private int _feeds;
    private int _trainingScore;
    private int _rallyHits;

    public TennisSim(SportSetup setup) : base(setup)
    {
        Scorer = new TennisScorer(2);
        ResetForServe();
    }

    public TennisScorer Scorer { get; }
    public TennisPhase Phase { get; private set; }
    public IReadOnlyList<Player> Players => _players;
    public V3 Ball => _ball;
    public V3 BallVelocity => _velocity;
    public bool IsTraining => Setup.Mode == SportMode.Training;
    public int TrainingScore => _trainingScore;
    public int BallsLeft => Math.Max(0, TrainingBalls - _feeds);
    public int Server => IsTraining ? 1 : Scorer.Server;
    public double PhaseTime => _phaseTime;

    /// <summary>+1 for the near player (plays toward +z), -1 for the far player.</summary>
    public static double Forward(int player) => player == 0 ? 1 : -1;

    public static double BaselineZ(int player) => -Forward(player) * (HalfLength + 0.6);

    public override string Scoreline => IsTraining
        ? $"Balls left {BallsLeft}  ·  Score {_trainingScore}"
        : $"Games {Scorer.Games(0)} - {Scorer.Games(1)}  ·  {Scorer.Call(p => p == 0 ? "P1" : "P2")}";

    protected override void Step(double dt, IReadOnlyList<PlayerCommand> commands)
    {
        _commands = commands;
        _phaseTime += dt;
        switch (Phase)
        {
            case TennisPhase.Serve: StepServe(); break;
            case TennisPhase.Toss: StepToss(dt); break;
            case TennisPhase.Rally: StepRally(dt); break;
            case TennisPhase.PointOver: StepPointOver(dt); break;
        }

        foreach (var p in _players)
        {
            if (p.SwingTime < 0) continue;
            p.SwingTime += dt;
            if (p.SwingTime > 0.55) p.SwingTime = -1;
        }

        var humanServer = !IsTraining && !Setup.IsCpu(Server);
        Prompt = Phase switch
        {
            TennisPhase.Serve when humanServer => "Press A or swing to toss the ball",
            TennisPhase.Toss when humanServer => "Swing at the top of the toss!",
            TennisPhase.Rally or TennisPhase.Serve when IsTraining => "Swing as the ball reaches you  ·  stick aims",
            TennisPhase.Rally => "Swing as the ball reaches you  ·  stick aims",
            _ => "",
        };
    }

    // ---------------------------------------------------------------- serving

    private void ResetForServe()
    {
        Phase = TennisPhase.Serve;
        _phaseTime = 0;
        _bounces = 0;
        _lastHitter = -1;
        _velocity = default;
        var server = Server;
        var pointsPlayed = IsTraining ? 0 : Scorer.Points(0) + Scorer.Points(1);
        var side = pointsPlayed % 2 == 0 ? 1 : -1;
        for (var i = 0; i < 2; i++)
        {
            var p = _players[i];
            p.SwingTime = -1;
            var fwd = Forward(i);
            var x = i == server ? side * 0.8 * fwd : -side * 1.5 * fwd;
            p.Position = new V3(x, 0, BaselineZ(i) - (i == server ? 0 : fwd * 0.4));
        }
        if (IsTraining) _players[0].Position = new V3(0, 0, BaselineZ(0));
        _ball = HandPosition(server);
    }

    private V3 HandPosition(int player)
    {
        var p = _players[player].Position;
        var fwd = Forward(player);
        return new V3(p.X + 0.3 * fwd, 1.0, p.Z + 0.35 * fwd);
    }

    private void StepServe()
    {
        if (IsTraining)
        {
            MovePlayers(1.0 / 60);
            if (_phaseTime > 1.1) FeedBall();
            return;
        }

        var server = Scorer.Server;
        _ball = HandPosition(server);
        var wants = Setup.IsCpu(server) ? _phaseTime > 1.3 : _phaseTime > 0.35 && CommandFor(_commands, server).SwingNow;
        if (!wants) return;
        Phase = TennisPhase.Toss;
        _phaseTime = 0;
        _velocity = new V3(0, TossSpeed, 0);
        var apex = 1.0 + TossSpeed * TossSpeed / (2 * Ballistics.Gravity);
        _cpuTossHeight = apex - 0.15 + CpuError(0.3);
    }

    private void StepToss(double dt)
    {
        var server = Scorer.Server;
        _velocity.Y -= Ballistics.Gravity * dt;
        _ball.Y += _velocity.Y * dt;
        var cmd = CommandFor(_commands, server);
        var hit = Setup.IsCpu(server)
            ? _velocity.Y < 0 && _ball.Y <= _cpuTossHeight
            : _phaseTime > 0.1 && cmd.SwingNow;
        if (hit)
        {
            ServeHit(server, cmd);
            return;
        }
        if (_ball.Y < 0.85 && _velocity.Y < 0)
        {
            // Dropped toss: just try again.
            Phase = TennisPhase.Serve;
            _phaseTime = 0.2;
        }
    }

    private void ServeHit(int server, PlayerCommand cmd)
    {
        var apex = 1.0 + TossSpeed * TossSpeed / (2 * Ballistics.Gravity);
        double quality;
        double power;
        if (Setup.IsCpu(server))
        {
            quality = Math.Clamp(0.55 + Setup.Difficulty * 0.12 + CpuError(0.15), 0, 1);
            power = 0.55 + Setup.Difficulty * 0.1;
        }
        else
        {
            quality = Math.Clamp(1 - Math.Abs(_ball.Y - apex) / 0.9, 0, 1);
            power = cmd.Swing > 0 ? cmd.Swing : 0.65;
        }

        var fwd = Forward(server);
        var serverX = _players[server].Position.X;
        _serveBoxSign = serverX >= 0 ? -1 : 1;
        var error = (1 - quality) * 1.6;
        var target = new V3(
            _serveBoxSign * Range(0.7, 3.2) + Range(-1, 1) * error,
            0,
            fwd * (ServiceLine - Range(0.7, 2.2) + Range(-1, 1) * error));
        Launch(server, target, Lerp(1.05, 0.72, power * (0.4 + 0.6 * quality)), allowNetFailure: quality < 0.2);
        _serving = true;
        _rallyHits = 0;
        var p = _players[server];
        p.SwingTime = IdealSwingTime;
        p.SwingSide = 1;
        p.HasHit = true;
        Emit(power * quality > 0.75 ? SportEventKind.PowerHit : SportEventKind.Hit, server, null, (float)(power * quality));
        Emit(SportEventKind.Serve, server);
        Phase = TennisPhase.Rally;
        _phaseTime = 0;
    }

    private void FeedBall()
    {
        _ball = new V3(Range(-1.5, 1.5), 1.0, HalfLength - 0.5);
        var target = new V3(Range(-3.2, 3.2), 0, Range(-9.8, -6.5));
        Launch(1, target, Range(1.05, 1.3), allowNetFailure: false);
        _serving = false;
        _rallyHits = 0;
        _feeds++;
        Phase = TennisPhase.Rally;
        _phaseTime = 0;
        Emit(SportEventKind.Serve, 1);
    }

    private void Launch(int hitter, V3 target, double seconds, bool allowNetFailure)
    {
        var v = Ballistics.LaunchVelocity(_ball, target, seconds);
        if (!allowNetFailure)
        {
            for (var i = 0; i < 10 && Ballistics.HeightAtZ(_ball, v, 0) < NetHeight + 0.22; i++)
            {
                seconds += 0.07;
                v = Ballistics.LaunchVelocity(_ball, target, seconds);
            }
        }
        _velocity = v;
        _lastHitter = hitter;
        _bounces = 0;
        foreach (var (p, i) in _players.Select((p, i) => (p, i)))
        {
            if (i == hitter) continue;
            p.HasHit = false;
            p.CpuTimingError = Setup.IsCpu(i) ? CpuError(0.07) + (Rng.NextDouble() < 0.12 - Setup.Difficulty * 0.03 ? 0.28 : 0) : 0;
        }
    }

    // ---------------------------------------------------------------- rally

    private void StepRally(double dt)
    {
        MovePlayers(dt);
        StartSwings();

        const int substeps = 3;
        var h = dt / substeps;
        for (var s = 0; s < substeps && Phase == TennisPhase.Rally; s++)
        {
            var previousZ = _ball.Z;
            _velocity.Y -= Ballistics.Gravity * h;
            var drag = 1 - 0.1 * h;
            _velocity.X *= drag;
            _velocity.Z *= drag;
            _ball += _velocity * h;

            if (previousZ != 0 && Math.Sign(previousZ) != Math.Sign(_ball.Z) && _ball.Y < NetHeight)
            {
                _ball.Z = previousZ > 0 ? 0.06 : -0.06;
                _velocity = new V3(0, Math.Min(0, _velocity.Y) * 0.3, 0);
                Emit(SportEventKind.Net, _lastHitter, _serving ? "Fault" : "Net");
                HitterFails();
                break;
            }

            if (_ball.Y <= BallRadius && _velocity.Y < 0) Bounce();
            if (Phase != TennisPhase.Rally) break;
            TryContacts();

            if (Math.Abs(_ball.Z) > HalfLength + 7 || Math.Abs(_ball.X) > HalfWidth + 8)
            {
                if (_bounces == 0)
                {
                    Emit(SportEventKind.Out, _lastHitter, "Out");
                    HitterFails();
                }
                else
                {
                    EndRally(_lastHitter);
                }
                break;
            }
        }
    }

    private void Bounce()
    {
        _ball.Y = BallRadius;
        _velocity.Y = -_velocity.Y * 0.7;
        _velocity.X *= 0.85;
        _velocity.Z *= 0.85;
        _bounces++;
        Emit(SportEventKind.Bounce, -1, null, (float)Math.Min(1, Math.Abs(_velocity.Y) / 6));

        var sideOwner = _ball.Z < 0 ? 0 : 1;
        if (_bounces == 1)
        {
            var receiver = 1 - _lastHitter;
            var inside = Math.Abs(_ball.X) <= HalfWidth + BallRadius && Math.Abs(_ball.Z) <= HalfLength + BallRadius;
            if (_serving)
                inside &= Math.Abs(_ball.Z) <= ServiceLine + BallRadius && Math.Sign(_ball.X) != -_serveBoxSign;
            if (sideOwner != receiver || !inside)
            {
                Emit(_serving ? SportEventKind.Fault : SportEventKind.Out, _lastHitter, _serving ? "Fault" : "Out");
                HitterFails();
                return;
            }
            if (IsTraining && _lastHitter == 0) ScoreTrainingReturn();
        }
        else
        {
            if (IsTraining && _lastHitter == 0)
            {
                EndRally(-1);
                return;
            }
            EndRally(_lastHitter);
        }
    }

    private void HitterFails()
    {
        if (IsTraining)
        {
            if (_lastHitter == 0) Emit(SportEventKind.Miss, 0, "Out");
            EndRally(IsTraining && _lastHitter == 1 ? 1 : -1);
            return;
        }
        if (_serving)
        {
            _faults++;
            if (_faults >= 2)
            {
                _faults = 0;
                _secondServe = false;
                Emit(SportEventKind.Announce, _lastHitter, "Double fault");
                EndRally(1 - _lastHitter);
            }
            else
            {
                _secondServe = true;
                Phase = TennisPhase.PointOver;
                _phaseTime = 0.6;
                _serving = false;
            }
            return;
        }
        EndRally(1 - _lastHitter);
    }

    private void ScoreTrainingReturn()
    {
        var inCourt = Math.Abs(_ball.X) <= HalfWidth && _ball.Z > 0 && _ball.Z <= HalfLength;
        if (!inCourt)
        {
            Emit(SportEventKind.Miss, 0, "Out");
            return;
        }
        var points = 1;
        foreach (var (x, z, r) in Targets)
        {
            var dx = _ball.X - x;
            var dz = _ball.Z - z;
            if (dx * dx + dz * dz <= r * r) points = 3;
        }
        _trainingScore += points;
        Emit(SportEventKind.Target, 0, points == 3 ? "Target! +3" : "+1", points / 3f);
    }

    private void EndRally(int winner)
    {
        if (Phase == TennisPhase.PointOver) return;
        Phase = TennisPhase.PointOver;
        _phaseTime = 0;
        _serving = false;
        if (IsTraining)
        {
            if (winner == 1) Emit(SportEventKind.Miss, 0, "Missed");
            return;
        }
        if (winner < 0) return;

        _faults = 0;
        _secondServe = false;
        var gameEnded = Scorer.PointTo(winner);
        if (Scorer.Winner is not null) Emit(SportEventKind.Game, winner, "Game, set and match!");
        else if (gameEnded) Emit(SportEventKind.Game, winner, $"Game P{winner + 1}  ({Scorer.Games(0)}-{Scorer.Games(1)})");
        else Emit(SportEventKind.Point, winner, Scorer.Call(p => p == 0 ? "P1" : "P2"));
        Emit(SportEventKind.Cheer, winner);
    }

    private void StepPointOver(double dt)
    {
        _velocity.Y -= Ballistics.Gravity * dt;
        _ball += _velocity * dt;
        if (_ball.Y < BallRadius)
        {
            _ball.Y = BallRadius;
            _velocity = new V3(_velocity.X * 0.8, Math.Abs(_velocity.Y) * 0.45, _velocity.Z * 0.8);
        }
        MovePlayers(dt);
        if (_phaseTime < 1.5) return;

        if (IsTraining)
        {
            if (_feeds >= TrainingBalls) Finish();
            else ResetForServe();
            return;
        }
        if (Scorer.Winner is not null)
        {
            Finish();
            return;
        }
        ResetForServe();
        if (_secondServe) Emit(SportEventKind.Announce, Scorer.Server, "Second serve");
    }

    // ---------------------------------------------------------------- movement and swings

    private void MovePlayers(double dt)
    {
        for (var i = 0; i < 2; i++)
        {
            if (IsTraining && i == 1) continue;
            var p = _players[i];
            var fwd = Forward(i);
            var target = new V3(0, 0, BaselineZ(i) + fwd * 0.3);
            if (Phase == TennisPhase.Rally && _lastHitter != i && PredictIntercept(i, out var hit))
            {
                var side = hit.X >= p.Position.X ? 1 : -1;
                p.SwingSide = side;
                target = new V3(hit.X - side * 0.62, 0, hit.Z - fwd * 0.25);
            }
            target.Z = fwd > 0 ? Math.Clamp(target.Z, -(HalfLength + 3), -0.8) : Math.Clamp(target.Z, 0.8, HalfLength + 3);
            target.X = Math.Clamp(target.X, -(HalfWidth + 2.5), HalfWidth + 2.5);

            var speed = Setup.IsCpu(i) ? 5.2 + Setup.Difficulty * 0.6 : 6.6;
            var d = target - p.Position;
            d.Y = 0;
            var length = d.FlatLength;
            p.Speed = length > 0.08 ? speed : 0;
            if (length > 1e-4) p.Position += d * (Math.Min(length, speed * dt) / length);
        }
    }

    private bool PredictIntercept(int player, out V3 point)
    {
        point = default;
        var fwd = Forward(player);
        if (_velocity.Z * fwd >= 0) return false;
        var pos = _ball;
        var vel = _velocity;
        var bounces = _bounces;
        const double h = 1.0 / 60;
        for (var k = 0; k < 240; k++)
        {
            vel.Y -= Ballistics.Gravity * h;
            pos += vel * h;
            if (pos.Y <= BallRadius && vel.Y < 0)
            {
                pos.Y = BallRadius;
                vel.Y = -vel.Y * 0.7;
                vel.X *= 0.85;
                vel.Z *= 0.85;
                bounces++;
            }
            var onSide = pos.Z * fwd < 0;
            if (onSide && bounces >= 1 && pos.Y > 0.45 && pos.Y < 1.5 && vel.Y < 0)
            {
                point = pos;
                return true;
            }
            if (onSide && Math.Abs(pos.Z) > HalfLength + 1.8 || bounces >= 2)
            {
                point = pos;
                return onSide;
            }
        }
        return false;
    }

    private V3 ContactPoint(int player)
    {
        var p = _players[player];
        return new V3(p.Position.X + p.SwingSide * 0.62, 1.0, p.Position.Z + Forward(player) * 0.25);
    }

    private void StartSwings()
    {
        for (var i = 0; i < 2; i++)
        {
            if (IsTraining && i == 1) continue;
            var p = _players[i];
            if (p.SwingTime >= 0) continue;
            if (Setup.IsCpu(i))
            {
                if (CpuWantsSwing(i)) StartSwing(i, (float)(0.6 + Setup.Difficulty * 0.09));
            }
            else
            {
                var c = CommandFor(_commands, i);
                if (c.SwingNow) StartSwing(i, c.Swing > 0 ? c.Swing : 0.65f);
            }
        }
    }

    private void StartSwing(int player, float power)
    {
        var p = _players[player];
        p.SwingTime = 0;
        p.SwingPower = power;
        Emit(SportEventKind.Swing, player);
    }

    private bool CpuWantsSwing(int player)
    {
        var p = _players[player];
        if (_lastHitter == player || p.HasHit || (_serving && _bounces == 0 && _velocity.Y > 0)) return false;
        var contact = ContactPoint(player);
        var pos = _ball;
        var vel = _velocity;
        var bounces = _bounces;
        const double h = 1.0 / 120;
        var wanted = IdealSwingTime + p.CpuTimingError;
        for (var k = 0; k * h <= wanted + 0.02; k++)
        {
            if (k > 0)
            {
                vel.Y -= Ballistics.Gravity * h;
                pos += vel * h;
                if (pos.Y <= BallRadius && vel.Y < 0)
                {
                    pos.Y = BallRadius;
                    vel.Y = -vel.Y * 0.7;
                    bounces++;
                }
            }
            if (_serving && bounces == 0) continue;
            var d = pos - contact;
            if (Math.Sqrt(d.X * d.X + d.Z * d.Z * 0.6) < ReachRadius * 0.55 && Math.Abs(k * h - wanted) < 0.02) return true;
        }
        return false;
    }

    private void TryContacts()
    {
        for (var i = 0; i < 2; i++)
        {
            if (IsTraining && i == 1) continue;
            var p = _players[i];
            if (p.SwingTime < 0 || p.HasHit || p.SwingTime > 0.34 || _lastHitter == i) continue;
            if (_serving && _bounces == 0) continue;
            if (_bounces > 1) continue;
            if (_ball.Z * Forward(i) > 0.5) continue;
            var contact = ContactPoint(i);
            var d = _ball - contact;
            if (Math.Sqrt(d.X * d.X + d.Z * d.Z * 0.6) > ReachRadius || _ball.Y < 0.1 || _ball.Y > 2.6) continue;
            PlayerHit(i);
            return;
        }
    }

    private void PlayerHit(int player)
    {
        var p = _players[player];
        p.HasHit = true;
        var timing = p.SwingTime - IdealSwingTime;
        var quality = Math.Clamp(1 - Math.Abs(timing) / 0.2, 0, 1);
        var fwd = Forward(player);

        double aimX;
        if (Setup.IsCpu(player))
        {
            aimX = Range(-3.2, 3.2) * (0.55 + Setup.Difficulty * 0.14);
        }
        else
        {
            aimX = CommandFor(_commands, player).MoveX * 3.0 * fwd;
        }
        aimX += -timing * 16 * p.SwingSide * fwd;
        aimX = Math.Clamp(aimX, -3.9, 3.9);

        var error = (1 - quality) * 2.4;
        _rallyHits++;
        if (Setup.IsCpu(player))
        {
            // Computer players make the occasional unforced error, more often as rallies drag on.
            var unforced = 0.07 - Setup.Difficulty * 0.012 + Math.Max(0, _rallyHits - 6) * 0.025;
            if (Rng.NextDouble() < unforced) error += Range(2.5, 4.0);
        }
        var target = new V3(aimX + Range(-1, 1) * error, 0, fwd * (HalfLength - Range(1.0, 4.5) + Range(-1, 1) * error * 0.8));
        var power = p.SwingPower;
        var strength = Math.Clamp(power * (0.35 + 0.65 * quality), 0, 1);
        Launch(player, target, Lerp(1.4, 0.8, strength), allowNetFailure: quality < 0.12);
        _serving = false;
        Emit(strength > 0.72 ? SportEventKind.PowerHit : SportEventKind.Hit, player, quality > 0.88 ? "Nice shot!" : null, (float)strength);
    }

    private void Finish()
    {
        if (IsTraining)
        {
            Result = new SportResult(SportKind.Tennis, SportMode.Training, new[] { _trainingScore }, null,
                $"{_trainingScore} points", $"{TrainingBalls} balls from the machine", _trainingScore);
        }
        else
        {
            var winner = Scorer.Winner ?? (Scorer.Games(0) >= Scorer.Games(1) ? 0 : 1);
            var g0 = Scorer.Games(0);
            var g1 = Scorer.Games(1);
            Result = new SportResult(SportKind.Tennis, SportMode.Match, new[] { g0, g1 }, winner,
                $"P{winner + 1} wins!", $"Games {g0} - {g1}", Margin: Math.Abs(g0 - g1));
        }
        Emit(SportEventKind.Finish);
    }
}
