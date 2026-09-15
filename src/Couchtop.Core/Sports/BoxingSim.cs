namespace Couchtop.Core.Sports;

/// <summary>
/// Three quick rounds of boxing. Punch left and right (bumpers, Q/E, mouse buttons or Wii Remote swings),
/// hold B to block, lean with the stick to dodge. Dodged punches leave the attacker open for a counter.
/// Three knockdowns end the fight. Training: hit the lit mitt as fast as you can.
/// </summary>
public sealed class BoxingSim : SportSim
{
    public const double RoundSeconds = 60;
    public const int Rounds = 3;
    public const double ImpactTime = 0.15;
    public const double PunchDuration = 0.42;
    public const double TrainingSeconds = 30;

    public enum BoxingPhase { Intro, Fight, KnockDown, RoundBreak }

    public sealed class Fighter
    {
        public double Health = 100;

        /// <summary>-1..1, leaning to dodge.</summary>
        public double Lean;

        public bool Blocking;
        public int PunchSide = 1;

        /// <summary>Seconds since the punch started, or -1.</summary>
        public double PunchTime = -1;

        /// <summary>Computer wind-up before a punch (a visible tell), or -1.</summary>
        public double WindUp = -1;

        public int KnockDowns;
        public bool Down;
        public double Stun;

        /// <summary>1 right after taking a hit, fading to 0.</summary>
        public double Flash;

        public int Hits;
        public double DamageDealt;
        internal float PunchPower = 0.6f;
        internal bool Resolved;
        internal bool CpuReacted;
        internal double LeanTarget;
        internal double DefenseTimer;
        internal double NextThink = 1;
        internal int LastSide = 1;
    }

    private readonly Fighter[] _fighters = { new(), new() };
    private IReadOnlyList<PlayerCommand> _commands = Array.Empty<PlayerCommand>();
    private double _phaseTime;
    private int _downFighter = -1;
    private int _count;
    private double _getUpAt;
    private double _mittTimer;
    private int _trainingHits;

    public BoxingSim(SportSetup setup) : base(setup)
    {
        RoundTime = IsTraining ? TrainingSeconds : RoundSeconds;
        Phase = BoxingPhase.Intro;
    }

    public IReadOnlyList<Fighter> Fighters => _fighters;
    public BoxingPhase Phase { get; private set; }
    public double PhaseTime => _phaseTime;
    public int Round { get; private set; } = 1;
    public double RoundTime { get; private set; }
    public bool IsTraining => Setup.Mode == SportMode.Training;
    public int Count => _count;
    public int DownFighter => _downFighter;

    /// <summary>Training: which mitt is lit (-1 left, +1 right, 0 none).</summary>
    public int MittSide { get; private set; }

    public int TrainingHits => _trainingHits;

    public override string Scoreline => IsTraining
        ? $"Time {Math.Max(0, RoundTime):0}  ·  Hits {_trainingHits}"
        : $"Round {Round}/{Rounds}  ·  {Math.Max(0, RoundTime):0}s  ·  P1 {Math.Max(0, _fighters[0].Health):0}%  ·  P2 {Math.Max(0, _fighters[1].Health):0}%";

    private bool IsHuman(int fighter) => !(IsTraining && fighter == 1) && !Setup.IsCpu(fighter);

    protected override void Step(double dt, IReadOnlyList<PlayerCommand> commands)
    {
        _commands = commands;
        _phaseTime += dt;
        foreach (var f in _fighters)
        {
            f.Lean += (f.LeanTarget - f.Lean) * Math.Min(1, dt * 10);
            f.Stun = Math.Max(0, f.Stun - dt);
            f.Flash = Math.Max(0, f.Flash - dt * 3);
        }

        switch (Phase)
        {
            case BoxingPhase.Intro:
                if (_phaseTime > (Round == 1 ? 2.0 : 1.2))
                {
                    Phase = BoxingPhase.Fight;
                    _phaseTime = 0;
                    Emit(SportEventKind.Bell, -1, IsTraining ? "Go!" : Round == 1 ? "Fight!" : $"Round {Round}");
                }
                break;
            case BoxingPhase.Fight: StepFight(dt); break;
            case BoxingPhase.KnockDown: StepKnockDown(); break;
            case BoxingPhase.RoundBreak:
                if (_phaseTime > 2.5)
                {
                    Round++;
                    RoundTime = RoundSeconds;
                    foreach (var f in _fighters)
                    {
                        f.Health = Math.Min(100, f.Health + 20);
                        f.PunchTime = -1;
                        f.WindUp = -1;
                    }
                    Phase = BoxingPhase.Intro;
                    _phaseTime = 0;
                }
                break;
        }

        Prompt = Phase switch
        {
            BoxingPhase.KnockDown when _downFighter >= 0 && IsHuman(_downFighter) => "Mash A to get up!",
            BoxingPhase.Fight or BoxingPhase.Intro when IsTraining => "Punch the lit mitt: left or right",
            BoxingPhase.Fight or BoxingPhase.Intro => "Punch left/right  ·  hold B to block  ·  stick to dodge",
            _ => "",
        };
    }

    private void StepFight(double dt)
    {
        RoundTime -= dt;
        for (var i = 0; i < 2; i++)
        {
            if (IsTraining && i == 1) continue;
            if (IsHuman(i)) ControlHuman(i, CommandFor(_commands, i));
            else ControlCpu(i, dt);
        }
        for (var i = 0; i < 2 && Phase == BoxingPhase.Fight; i++) AdvancePunch(i, dt);
        if (IsTraining) UpdateMitts(dt);

        if (Phase != BoxingPhase.Fight) return;
        if (IsTraining && RoundTime <= 0)
        {
            Emit(SportEventKind.Bell, -1, "Time!");
            Result = new SportResult(SportKind.Boxing, SportMode.Training, new[] { _trainingHits }, null,
                $"{_trainingHits} hits", "Mitt Drill", _trainingHits);
            Emit(SportEventKind.Finish);
        }
        else if (!IsTraining && RoundTime <= 0)
        {
            Emit(SportEventKind.Bell, -1, Round >= Rounds ? "That's the fight!" : "End of the round");
            if (Round >= Rounds) Decision();
            else
            {
                Phase = BoxingPhase.RoundBreak;
                _phaseTime = 0;
            }
        }
    }

    private void ControlHuman(int i, PlayerCommand c)
    {
        var f = _fighters[i];
        if (f.Down) return;
        f.Blocking = c.Alt || c.MoveY < -0.6;
        f.LeanTarget = f.Blocking ? 0 : Math.Clamp(c.MoveX, -1, 1);
        if (f.PunchTime >= 0 || f.Stun > 0 || f.Blocking) return;

        var side = 0;
        var power = 0.6f;
        if (c.LeftPunch) side = -1;
        else if (c.RightPunch) side = 1;
        else if (c.Swing > 0)
        {
            side = c.SwingSide != 0 ? c.SwingSide : -f.LastSide;
            power = 0.45f + c.Swing * 0.55f;
        }
        else if (c.ActionPressed) side = -f.LastSide;
        if (side != 0) StartPunch(i, side, power);
    }

    private void ControlCpu(int i, double dt)
    {
        var f = _fighters[i];
        if (f.Down) return;

        if (IsTraining)
        {
            if (f.PunchTime < 0 && MittSide != 0 && _mittTimer > 0.2 + (3 - Setup.Difficulty) * 0.08) StartPunch(i, MittSide, 0.7f);
            return;
        }

        var o = _fighters[1 - i];
        if (f.DefenseTimer > 0)
        {
            f.DefenseTimer -= dt;
            if (f.DefenseTimer <= 0)
            {
                f.Blocking = false;
                f.LeanTarget = 0;
            }
        }

        if (o.PunchTime >= 0 && !o.CpuReacted)
        {
            o.CpuReacted = true;
            var roll = Rng.NextDouble();
            var block = 0.2 + Setup.Difficulty * 0.12 + (f.Health < 35 ? 0.1 : 0);
            var dodge = 0.08 + Setup.Difficulty * 0.07;
            if (roll < block)
            {
                f.Blocking = true;
                f.WindUp = -1;
                f.DefenseTimer = 0.45;
            }
            else if (roll < block + dodge)
            {
                f.LeanTarget = Rng.Next(2) == 0 ? -1 : 1;
                f.DefenseTimer = 0.45;
            }
        }

        if (f.Blocking || f.Stun > 0) return;
        if (f.WindUp >= 0)
        {
            f.WindUp += dt;
            if (f.WindUp >= 0.42 - Setup.Difficulty * 0.07)
            {
                f.WindUp = -1;
                StartPunch(i, Rng.Next(2) == 0 ? -1 : 1, 0.5f + Setup.Difficulty * 0.1f);
            }
            return;
        }

        f.NextThink -= dt;
        if (f.NextThink <= 0 && f.PunchTime < 0)
        {
            f.NextThink = Range(0.5, 1.2) - Setup.Difficulty * 0.12;
            if (Rng.NextDouble() < 0.72) f.WindUp = 0;
        }
    }

    private void StartPunch(int i, int side, float power)
    {
        var f = _fighters[i];
        f.PunchTime = 0;
        f.PunchSide = side;
        f.LastSide = side;
        f.PunchPower = power;
        f.Resolved = false;
        f.CpuReacted = false;
        Emit(SportEventKind.Punch, i);
    }

    private void AdvancePunch(int i, double dt)
    {
        var f = _fighters[i];
        if (f.PunchTime < 0) return;
        f.PunchTime += dt;
        if (!f.Resolved && f.PunchTime >= ImpactTime)
        {
            f.Resolved = true;
            ResolvePunch(i);
        }
        if (f.PunchTime >= PunchDuration) f.PunchTime = -1;
    }

    private void ResolvePunch(int i)
    {
        var f = _fighters[i];
        if (IsTraining)
        {
            if (MittSide != 0 && MittSide == f.PunchSide)
            {
                _trainingHits++;
                MittSide = 0;
                _mittTimer = 0;
                Emit(SportEventKind.Target, 0, null, 1);
            }
            else
            {
                Emit(SportEventKind.Miss, 0);
            }
            return;
        }

        var o = _fighters[1 - i];
        if (o.Down) return;
        if (o.Blocking)
        {
            o.Health -= 1.5;
            f.Stun = 0.25;
            Emit(SportEventKind.Block, 1 - i);
            return;
        }
        if (Math.Abs(o.Lean) > 0.55 && Rng.NextDouble() < 0.85)
        {
            f.Stun = 0.35;
            Emit(SportEventKind.Dodge, 1 - i, "Dodged!");
            return;
        }

        var counter = o.PunchTime >= 0 && !o.Resolved;
        var damage = (7 + 5 * f.PunchPower) * (counter ? 1.6 : 1);
        o.Health -= damage;
        o.Flash = 1;
        o.WindUp = -1;
        f.Hits++;
        f.DamageDealt += damage;
        if (counter)
        {
            o.Stun = 0.5;
            o.PunchTime = -1;
            Emit(SportEventKind.PunchHit, i, "Counter!", 1);
        }
        else
        {
            Emit(SportEventKind.PunchHit, i, null, f.PunchPower);
        }
        if (o.Health <= 0) KnockDown(1 - i);
    }

    private void KnockDown(int fighter)
    {
        var o = _fighters[fighter];
        o.Health = 0;
        o.Down = true;
        o.KnockDowns++;
        o.PunchTime = -1;
        o.WindUp = -1;
        o.Blocking = false;
        _fighters[1 - fighter].PunchTime = -1;
        _downFighter = fighter;
        _count = 0;
        _getUpAt = o.KnockDowns >= 3 ? 99 : Math.Min(9, 3 + o.KnockDowns * 2 + Rng.Next(0, 3));
        Phase = BoxingPhase.KnockDown;
        _phaseTime = 0;
        Emit(SportEventKind.KnockDown, fighter, o.KnockDowns >= 3 ? "Down for good!" : "Down!");
        Emit(SportEventKind.Cheer, 1 - fighter);
    }

    private void StepKnockDown()
    {
        if (IsHuman(_downFighter))
        {
            var c = CommandFor(_commands, _downFighter);
            if (c.ActionPressed || c.Swing > 0 || c.LeftPunch || c.RightPunch) _getUpAt = Math.Max(2, _getUpAt - 0.22);
        }
        if (_phaseTime < 0.65) return;
        _phaseTime = 0;
        _count++;
        Emit(SportEventKind.Announce, -1, _count.ToString());
        if (_count >= 10)
        {
            Finish(1 - _downFighter, "K.O.!");
            return;
        }
        if (_count >= _getUpAt)
        {
            var f = _fighters[_downFighter];
            f.Down = false;
            f.Health = Math.Max(25, 60 - 20 * (f.KnockDowns - 1));
            Phase = BoxingPhase.Fight;
            Emit(SportEventKind.GetUp, _downFighter, "Back up!");
            _downFighter = -1;
        }
    }

    private void Decision()
    {
        var a = _fighters[0];
        var b = _fighters[1];
        int? winner = a.KnockDowns < b.KnockDowns ? 0
            : b.KnockDowns < a.KnockDowns ? 1
            : a.DamageDealt > b.DamageDealt + 0.5 ? 0
            : b.DamageDealt > a.DamageDealt + 0.5 ? 1
            : null;
        Finish(winner, "decision");
    }

    private void Finish(int? winner, string how)
    {
        if (IsFinished) return;
        var scores = new[] { _fighters[1].KnockDowns, _fighters[0].KnockDowns };
        var headline = winner is null ? "It's a draw!" : how == "K.O.!" ? $"K.O.! P{winner + 1} wins!" : $"P{winner + 1} wins by decision";
        Result = new SportResult(SportKind.Boxing, SportMode.Match, scores, winner, headline,
            $"Knockdowns {scores[0]} - {scores[1]}  ·  Punches landed {_fighters[0].Hits} - {_fighters[1].Hits}",
            Margin: Math.Abs(scores[0] - scores[1]) + (how == "K.O.!" ? 2 : 0));
        Emit(SportEventKind.Finish);
    }

    private void UpdateMitts(double dt)
    {
        _mittTimer += dt;
        if (MittSide == 0)
        {
            if (_mittTimer >= 0.25)
            {
                MittSide = Rng.Next(2) == 0 ? -1 : 1;
                _mittTimer = 0;
            }
            return;
        }
        var window = Math.Max(0.55, 1.2 - _trainingHits * 0.02);
        if (_mittTimer > window)
        {
            MittSide = 0;
            _mittTimer = 0;
            Emit(SportEventKind.Miss, 0);
        }
    }
}
