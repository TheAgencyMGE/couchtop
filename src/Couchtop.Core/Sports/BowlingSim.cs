namespace Couchtop.Core.Sports;

/// <summary>
/// Ten-pin bowling for up to four players taking turns. Move and angle the ball with the stick, hold A to
/// wind up a power meter and let go to roll (or just swing a Wii Remote), then steer to add curve.
/// Pins are simulated as colliding discs, so every roll plays out differently. Training: spare setups.
/// </summary>
public sealed class BowlingSim : SportSim
{
    public const double LaneHalfWidth = 0.533;
    public const double LaneLength = 18.29;
    public const double BallRadius = 0.109;
    public const double PinRadius = 0.06;
    public const double DeckEnd = 19.25;
    public const double GutterX = 0.63;
    public const double PinSpacing = 0.3048;
    public const double RowDepth = 0.264;

    public enum BowlingPhase { Aim, Charge, Rolling, Settle, Sweep }

    public sealed class Pin
    {
        public int Number;
        public double X, Z, Vx, Vz;
        public bool Down;

        /// <summary>Knocked off the deck or swept away: no longer drawn or simulated.</summary>
        public bool Gone;

        /// <summary>0 standing .. 1 lying flat (animation).</summary>
        public double Fall;

        public double FallAngle;
        internal double HomeX, HomeZ;
    }

    private static readonly int[][] SpareSetups =
    {
        new[] { 10 }, new[] { 7 }, new[] { 1, 2, 4 }, new[] { 3, 6, 10 },
        new[] { 5 }, new[] { 2, 8 }, new[] { 6, 10 }, new[] { 4, 7 },
    };

    private readonly Pin[] _pins = new Pin[10];
    private readonly bool[] _standingBefore = new bool[10];
    private IReadOnlyList<PlayerCommand> _commands = Array.Empty<PlayerCommand>();
    private double _phaseTime;
    private double _meterTime;
    private int _current;
    private bool _hitAny;
    private double _cpuTargetX;
    private double _cpuAngle;
    private double _cpuPower;
    private double _cpuSpin;
    private int _setupIndex;
    private int _cleared;

    public BowlingSim(SportSetup setup) : base(setup)
    {
        for (int row = 0, n = 0; row < 4; row++)
        {
            for (var k = 0; k <= row; k++, n++)
            {
                _pins[n] = new Pin
                {
                    Number = n + 1,
                    HomeX = (k - row / 2.0) * PinSpacing,
                    HomeZ = LaneLength + row * RowDepth,
                };
            }
        }

        Scorers = Enumerable.Range(0, Math.Max(1, setup.Players)).Select(_ => new BowlingScorer()).ToArray();
        if (IsTraining) LoadSpareSetup();
        else ResetRack(null);
        StartAim();
    }

    public BowlingScorer[] Scorers { get; }
    public IReadOnlyList<Pin> Pins => _pins;
    public BowlingPhase Phase { get; private set; }
    public double PhaseTime => _phaseTime;
    public bool IsTraining => Setup.Mode == SportMode.Training;
    public override int ActivePlayer => IsTraining ? 0 : _current;
    public double BallX { get; private set; }
    public double BallZ { get; private set; }
    public double BallVx { get; private set; }
    public double BallVz { get; private set; }
    public double BallSpin { get; private set; }
    public bool BallVisible => Phase is BowlingPhase.Aim or BowlingPhase.Charge || (Phase == BowlingPhase.Rolling && BallZ < DeckEnd + 0.3);
    public bool InGutter { get; private set; }
    public double AimX { get; private set; }
    public double AimAngle { get; private set; }
    public double Power { get; private set; }
    public int SetupNumber => _setupIndex + 1;
    public int Cleared => _cleared;

    public override string Scoreline
    {
        get
        {
            if (IsTraining) return $"Setup {Math.Min(_setupIndex + 1, SpareSetups.Length)}/{SpareSetups.Length}  ·  Cleared {_cleared}";
            var (frame, roll, _) = Scorers[_current].Position;
            return $"P{_current + 1}  ·  Frame {frame + 1}  ·  Ball {roll + 1}  ·  {Scorers[_current].Total} pts";
        }
    }

    private bool IsCpu => Setup.IsCpu(ActivePlayer);

    protected override void Step(double dt, IReadOnlyList<PlayerCommand> commands)
    {
        _commands = commands;
        _phaseTime += dt;
        var cmd = CommandFor(commands, ActivePlayer);
        switch (Phase)
        {
            case BowlingPhase.Aim: StepAim(dt, cmd); break;
            case BowlingPhase.Charge: StepCharge(dt, cmd); break;
            case BowlingPhase.Rolling:
                if (!IsCpu) BallSpin += (cmd.MoveX * 0.9 - BallSpin) * Math.Min(1, dt * 2.5);
                Simulate(dt);
                if (BallZ > DeckEnd + 0.3)
                {
                    Phase = BowlingPhase.Settle;
                    _phaseTime = 0;
                }
                else if (_phaseTime > 7)
                {
                    Phase = BowlingPhase.Settle;
                    _phaseTime = 0;
                }
                break;
            case BowlingPhase.Settle:
                Simulate(dt);
                if ((_phaseTime > 1.1 && PinsAtRest()) || _phaseTime > 3.2) FinishRoll();
                break;
            case BowlingPhase.Sweep:
                if (_phaseTime > 0.9) StartAim();
                break;
        }

        Prompt = IsCpu ? "" : Phase switch
        {
            BowlingPhase.Aim => "Stick moves  ·  up/down angles  ·  hold A (or swing) to bowl",
            BowlingPhase.Charge => "Let go of A at full power",
            BowlingPhase.Rolling => "Steer left or right to curve the ball",
            _ => "",
        };
    }

    // ---------------------------------------------------------------- rack & turns

    private void ResetRack(IReadOnlyCollection<int>? standing)
    {
        foreach (var pin in _pins)
        {
            var up = standing is null || standing.Contains(pin.Number);
            pin.X = pin.HomeX;
            pin.Z = pin.HomeZ;
            pin.Vx = pin.Vz = 0;
            pin.Down = !up;
            pin.Gone = !up;
            pin.Fall = up ? 0 : 1;
        }
    }

    private void LoadSpareSetup() => ResetRack(SpareSetups[Math.Min(_setupIndex, SpareSetups.Length - 1)]);

    private void StartAim()
    {
        Phase = BowlingPhase.Aim;
        _phaseTime = 0;
        Power = 0;
        BallZ = 0.2;
        BallVx = BallVz = BallSpin = 0;
        InGutter = false;
        _hitAny = false;
        for (var i = 0; i < 10; i++) _standingBefore[i] = !_pins[i].Down;
        if (IsCpu) PlanCpuRoll();
        else AimAngle = Math.Clamp(AimAngle, -0.05, 0.05);
        BallX = AimX;
    }

    private void PlanCpuRoll()
    {
        var standing = _pins.Where(p => !p.Down).ToList();
        if (standing.Count == 10)
        {
            _cpuTargetX = 0.09 + CpuError(0.1);
            _cpuSpin = -0.2 + CpuError(0.2);
        }
        else
        {
            _cpuTargetX = (standing.Count > 0 ? standing.Average(p => p.HomeX) : 0) + CpuError(0.09);
            _cpuSpin = CpuError(0.12);
        }
        _cpuTargetX = Math.Clamp(_cpuTargetX, -0.42, 0.42);
        _cpuAngle = CpuError(0.008);
        _cpuPower = Math.Clamp(0.72 + CpuError(0.14), 0.35, 1);
    }

    private void StepAim(double dt, PlayerCommand cmd)
    {
        if (IsCpu)
        {
            AimX += Math.Clamp(_cpuTargetX - AimX, -0.5 * dt, 0.5 * dt);
            AimAngle += Math.Clamp(_cpuAngle - AimAngle, -0.05 * dt, 0.05 * dt);
            BallX = AimX;
            if (_phaseTime > 1.4)
            {
                Power = _cpuPower;
                Release(_cpuSpin);
            }
            return;
        }

        AimX = Math.Clamp(AimX + cmd.MoveX * 0.55 * dt, -0.42, 0.42);
        AimAngle = Math.Clamp(AimAngle + cmd.MoveY * 0.06 * dt, -0.06, 0.06);
        BallX = AimX;
        if (cmd.Swing > 0)
        {
            Power = 0.25 + cmd.Swing * 0.75;
            Release(cmd.SwingSide * 0.25);
        }
        else if (cmd.ActionPressed && _phaseTime > 0.15)
        {
            Phase = BowlingPhase.Charge;
            _phaseTime = 0;
            _meterTime = 0;
        }
    }

    private void StepCharge(double dt, PlayerCommand cmd)
    {
        _meterTime += dt;
        Power = 0.5 - 0.5 * Math.Cos(_meterTime * Math.PI * 1.3);
        if ((cmd.ActionReleased && _phaseTime > 0.12) || (cmd.ActionPressed && _phaseTime > 0.2)) Release(0);
    }

    private void Release(double spin)
    {
        var speed = 5.0 + 4.0 * Power;
        BallVx = Math.Sin(AimAngle) * speed;
        BallVz = Math.Cos(AimAngle) * speed;
        BallSpin = spin;
        BallZ = 0.2;
        Phase = BowlingPhase.Rolling;
        _phaseTime = 0;
        Emit(SportEventKind.Roll, ActivePlayer, null, (float)Power);
    }

    // ---------------------------------------------------------------- physics

    private void Simulate(double dt)
    {
        const int substeps = 8;
        var h = dt / substeps;
        for (var s = 0; s < substeps; s++)
        {
            if (Phase == BowlingPhase.Rolling && BallZ <= DeckEnd + 0.3)
            {
                if (!InGutter && BallZ > 5 && BallZ < LaneLength - 0.4) BallVx += BallSpin * 0.42 * h;
                BallX += BallVx * h;
                BallZ += BallVz * h;
                if (!InGutter && Math.Abs(BallX) > LaneHalfWidth + 0.02 && BallZ < LaneLength - 0.3)
                {
                    InGutter = true;
                    BallX = Math.Sign(BallX) * GutterX;
                    BallVx = 0;
                    Emit(SportEventKind.Gutter, ActivePlayer, "Gutter");
                }
                if (!InGutter) CollideBall();
            }
            StepPins(h);
        }
    }

    private void CollideBall()
    {
        const double ballMass = 6.8, pinMass = 1.6, restitution = 0.7;
        foreach (var pin in _pins)
        {
            if (pin.Gone) continue;
            var dx = pin.X - BallX;
            var dz = pin.Z - BallZ;
            var min = BallRadius + PinRadius;
            var dist2 = dx * dx + dz * dz;
            if (dist2 >= min * min || dist2 < 1e-12) continue;
            var dist = Math.Sqrt(dist2);
            var nx = dx / dist;
            var nz = dz / dist;
            var approach = (pin.Vx - BallVx) * nx + (pin.Vz - BallVz) * nz;
            if (approach < 0)
            {
                var j = -(1 + restitution) * approach / (1 / ballMass + 1 / pinMass);
                BallVx -= j / ballMass * nx;
                BallVz -= j / ballMass * nz;
                pin.Vx += j / pinMass * nx;
                pin.Vz += j / pinMass * nz;
                Topple(pin, nx, nz);
                if (!_hitAny)
                {
                    _hitAny = true;
                    Emit(SportEventKind.PinHit, ActivePlayer, null, (float)Math.Min(1, -approach / 8));
                }
            }
            var overlap = min - dist;
            pin.X += nx * overlap;
            pin.Z += nz * overlap;
        }
    }

    private static void Topple(Pin pin, double nx, double nz)
    {
        if (pin.Down) return;
        pin.Down = true;
        pin.FallAngle = Math.Atan2(nx, nz);
    }

    private void StepPins(double h)
    {
        for (var i = 0; i < _pins.Length; i++)
        {
            var a = _pins[i];
            if (a.Gone) continue;
            var speed = Math.Sqrt(a.Vx * a.Vx + a.Vz * a.Vz);
            if (speed > 0)
            {
                var decel = (a.Down ? 2.2 : 4.5) * h;
                if (speed <= decel) a.Vx = a.Vz = 0;
                else
                {
                    var scale = (speed - decel) / speed;
                    a.Vx *= scale;
                    a.Vz *= scale;
                }
                if (!a.Down && speed > 0.35) Topple(a, a.Vx / speed, a.Vz / speed);
            }
            a.X += a.Vx * h;
            a.Z += a.Vz * h;
            if (a.Down && a.Fall < 1) a.Fall = Math.Min(1, a.Fall + h * 4);
            if (Math.Abs(a.X) > LaneHalfWidth + 0.25 || a.Z > DeckEnd + 0.3)
            {
                a.Down = true;
                a.Gone = true;
                continue;
            }

            for (var k = i + 1; k < _pins.Length; k++)
            {
                var b = _pins[k];
                if (b.Gone) continue;
                var reach = (a.Down ? 0.1 : PinRadius) + (b.Down ? 0.1 : PinRadius);
                var dx = b.X - a.X;
                var dz = b.Z - a.Z;
                var dist2 = dx * dx + dz * dz;
                if (dist2 >= reach * reach || dist2 < 1e-12) continue;
                var dist = Math.Sqrt(dist2);
                var nx = dx / dist;
                var nz = dz / dist;
                var approach = (b.Vx - a.Vx) * nx + (b.Vz - a.Vz) * nz;
                if (approach < 0)
                {
                    var j = -(1 + 0.6) * approach / 2;
                    a.Vx -= j * nx;
                    a.Vz -= j * nz;
                    b.Vx += j * nx;
                    b.Vz += j * nz;
                    if (j > 0.12)
                    {
                        Topple(b, nx, nz);
                        if (!a.Down) Topple(a, -nx, -nz);
                    }
                }
                var overlap = (reach - dist) / 2;
                a.X -= nx * overlap;
                a.Z -= nz * overlap;
                b.X += nx * overlap;
                b.Z += nz * overlap;
            }
        }
    }

    private bool PinsAtRest() => _pins.All(p => p.Gone || (Math.Abs(p.Vx) < 0.02 && Math.Abs(p.Vz) < 0.02 && (!p.Down || p.Fall >= 1)));

    // ---------------------------------------------------------------- scoring

    private void FinishRoll()
    {
        var knocked = 0;
        for (var i = 0; i < 10; i++)
            if (_standingBefore[i] && _pins[i].Down) knocked++;
        var standingAfter = _pins.Count(p => !p.Down);

        if (IsTraining)
        {
            if (standingAfter == 0)
            {
                _cleared++;
                Emit(SportEventKind.Spare, 0, "Cleared!");
                Emit(SportEventKind.Cheer, 0);
            }
            else
            {
                Emit(SportEventKind.Miss, 0, standingAfter == 1 ? "1 pin left" : $"{standingAfter} pins left");
            }
            _setupIndex++;
            if (_setupIndex >= SpareSetups.Length)
            {
                Finish();
                return;
            }
            LoadSpareSetup();
            Phase = BowlingPhase.Sweep;
            _phaseTime = 0;
            return;
        }

        var scorer = Scorers[_current];
        var before = scorer.Position;
        knocked = Math.Min(knocked, before.Standing);
        scorer.Roll(knocked);

        if (before.Standing == 10 && knocked == 10)
        {
            Emit(SportEventKind.BowlingStrike, _current, "Strike!");
            Emit(SportEventKind.Cheer, _current);
        }
        else if (before.Standing < 10 && knocked == before.Standing && knocked > 0)
        {
            Emit(SportEventKind.Spare, _current, "Spare!");
            Emit(SportEventKind.Cheer, _current);
        }
        else if (knocked == 0)
        {
            Emit(SportEventKind.Miss, _current, InGutter ? "Gutter ball" : "No pins");
        }
        else
        {
            Emit(SportEventKind.PinsFall, _current, knocked == 1 ? "1 pin" : $"{knocked} pins", knocked / 10f);
        }

        var after = scorer.Position;
        if (scorer.IsComplete || after.Frame != before.Frame)
        {
            if (!NextPlayer()) return;
        }
        else if (after.Standing == 10)
        {
            ResetRack(null);
        }
        else
        {
            foreach (var pin in _pins)
                if (pin.Down) pin.Gone = true;
        }
        Phase = BowlingPhase.Sweep;
        _phaseTime = 0;
    }

    private bool NextPlayer()
    {
        for (var k = 1; k <= Scorers.Length; k++)
        {
            var next = (_current + k) % Scorers.Length;
            if (Scorers[next].IsComplete) continue;
            _current = next;
            ResetRack(null);
            return true;
        }
        Finish();
        return false;
    }

    private void Finish()
    {
        if (IsFinished) return;
        if (IsTraining)
        {
            Result = new SportResult(SportKind.Bowling, SportMode.Training, new[] { _cleared }, null,
                $"{_cleared} of {SpareSetups.Length} cleared", "Spare Master", _cleared);
        }
        else
        {
            var totals = Scorers.Select(s => s.Total).ToArray();
            var best = totals.Max();
            int? winner = totals.Count(t => t == best) == 1 && totals.Length > 1 ? Array.IndexOf(totals, best) : null;
            var second = totals.Length > 1 ? totals.OrderByDescending(t => t).ElementAt(1) : best;
            var headline = totals.Length == 1 ? $"{best} pins" : winner is null ? "It's a tie!" : $"P{winner + 1} wins!";
            Result = new SportResult(SportKind.Bowling, SportMode.Match, totals, winner, headline,
                string.Join("   ", totals.Select((t, i) => $"P{i + 1}: {t}")), Margin: (best - second) / 20);
        }
        Emit(SportEventKind.Finish);
    }
}
