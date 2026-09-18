using Couchtop.Core.Channels;
using Couchtop.Core.Input;
using Couchtop.Core.Sports;

namespace Couchtop.Tests;

public class SportsScoringTests
{
    [Fact]
    public void Bowling_perfect_game_is_300()
    {
        var s = new BowlingScorer();
        for (var i = 0; i < 12; i++) s.Roll(10);
        Assert.True(s.IsComplete);
        Assert.Equal(300, s.Total);
        Assert.Equal("X X X", s.Marks(9));
    }

    [Fact]
    public void Bowling_all_spares_and_open_frames()
    {
        var spares = new BowlingScorer();
        for (var i = 0; i < 21; i++) spares.Roll(5);
        Assert.True(spares.IsComplete);
        Assert.Equal(150, spares.Total);
        Assert.Equal("5 /", spares.Marks(0));

        var open = new BowlingScorer();
        for (var i = 0; i < 20; i++) open.Roll(i % 2 == 0 ? 9 : 0);
        Assert.True(open.IsComplete);
        Assert.Equal(90, open.Total);
        Assert.Equal("9 -", open.Marks(3));
    }

    [Fact]
    public void Bowling_tracks_frames_bonuses_and_rejects_impossible_rolls()
    {
        var s = new BowlingScorer();
        s.Roll(10);
        Assert.Null(s.FrameTotals()[0]);
        s.Roll(7);
        Assert.Equal((1, 1, 3), s.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Roll(4));
        s.Roll(2);
        Assert.Equal(19, s.FrameTotals()[0]);
        Assert.Equal(28, s.FrameTotals()[1]);

        var spare = new BowlingScorer();
        spare.Roll(7);
        spare.Roll(3);
        Assert.Null(spare.FrameTotals()[0]);
        spare.Roll(4);
        Assert.Equal(14, spare.FrameTotals()[0]);
    }

    [Fact]
    public void Tennis_scoring_with_deuce_advantage_and_match()
    {
        var t = new TennisScorer(2);
        Assert.Equal("Love All", t.Call());
        t.PointTo(0);
        Assert.Equal("15 - Love", t.Call());
        for (var i = 0; i < 2; i++) t.PointTo(0);
        for (var i = 0; i < 3; i++) t.PointTo(1);
        Assert.Equal("Deuce", t.Call());
        t.PointTo(1);
        Assert.Equal("Advantage P2", t.Call());
        t.PointTo(0);
        Assert.Equal("Deuce", t.Call());
        t.PointTo(0);
        Assert.True(t.PointTo(0));
        Assert.Equal(1, t.Games(0));
        Assert.Equal(1, t.Server);

        for (var i = 0; i < 4; i++) t.PointTo(0);
        Assert.Equal(0, t.Winner);
        Assert.False(t.PointTo(1));
    }

    [Fact]
    public void Golf_totals_to_par_and_terms()
    {
        var g = new GolfScorer(2, new[] { 3, 4, 3 });
        g.Record(0, 0, 2);
        g.Record(0, 1, 5);
        g.Record(1, 0, 3);
        Assert.Equal(7, g.Total(0));
        Assert.Equal(0, g.ToPar(0));
        Assert.Equal("E", GolfScorer.ToParText(0));
        Assert.Equal("+2", GolfScorer.ToParText(2));
        Assert.Equal("Birdie!", GolfScorer.Term(2, 3));
        Assert.Equal("Hole in One!", GolfScorer.Term(1, 3));
        Assert.Equal("Double Bogey", GolfScorer.Term(6, 4));
    }

    [Theory]
    [InlineData(new[] { false, false, false }, 1, 0, new[] { true, false, false })]
    [InlineData(new[] { true, true, true }, 1, 1, new[] { true, true, true })]
    [InlineData(new[] { true, false, true }, 2, 1, new[] { false, true, true })]
    [InlineData(new[] { true, true, true }, 4, 4, new[] { false, false, false })]
    [InlineData(new[] { false, true, false }, 3, 1, new[] { false, false, true })]
    public void Base_runners_advance(bool[] bases, int gained, int expectedRuns, bool[] expectedBases)
    {
        var copy = bases.ToArray();
        Assert.Equal(expectedRuns, BaseRunners.Advance(copy, gained));
        Assert.Equal(expectedBases, copy);
    }
}

public class SportsProgressTests
{
    [Fact]
    public void Skill_moves_with_results_and_stays_in_range()
    {
        Assert.True(SkillRating.Update(100, won: true, margin: 2, difficulty: 1) > 100);
        Assert.True(SkillRating.Update(100, won: false, margin: 2, difficulty: 1) < 100);
        Assert.Equal(0, SkillRating.Update(5, won: false, margin: 5, difficulty: 0));
        Assert.Equal(SkillRating.Max, SkillRating.Update(990, won: true, margin: 5, difficulty: 3));
        Assert.Equal("Rookie", SkillRating.Level(0));
        Assert.Equal("Legend", SkillRating.Level(SkillRating.Max));
        Assert.InRange(SkillRating.DifficultyFor(SkillRating.Max), 0, 3);
    }

    [Theory]
    [InlineData(SportKind.Tennis)]
    [InlineData(SportKind.Baseball)]
    [InlineData(SportKind.Bowling)]
    [InlineData(SportKind.Golf)]
    [InlineData(SportKind.Boxing)]
    public void Training_medals_follow_goals(SportKind sport)
    {
        var goal = TrainingGoals.For(sport);
        Assert.True(goal.Bronze < goal.Silver && goal.Silver < goal.Gold);
        Assert.Equal(TrainingMedal.None, TrainingGoals.MedalFor(sport, goal.Bronze - 1));
        Assert.Equal(TrainingMedal.Bronze, TrainingGoals.MedalFor(sport, goal.Bronze));
        Assert.Equal(TrainingMedal.Gold, TrainingGoals.MedalFor(sport, goal.Gold + 10));
    }

    [Fact]
    public void Records_persist_matches_training_and_pals()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "sports.json");
        var service = new SportsRecordsService(path);
        var setup = new SportSetup(SportKind.Tennis, SportMode.Match, Players: 2, Humans: 1, Difficulty: 1);
        var update = service.Record(new SportResult(SportKind.Tennis, SportMode.Match, new[] { 2, 0 }, 0, "P1 wins!", "", Margin: 2), setup);
        Assert.True(update.SkillAfter > update.SkillBefore);
        Assert.True(update.NewBest);

        var golf = new SportSetup(SportKind.Golf, SportMode.Training, 1, 1);
        var goal = TrainingGoals.For(SportKind.Golf);
        var training = service.Record(new SportResult(SportKind.Golf, SportMode.Training, new[] { goal.Silver }, null, "", "", goal.Silver), golf);
        Assert.Equal(TrainingMedal.Silver, training.Medal);
        Assert.True(training.MedalImproved);
        service.Current.Pal(1).Shirt = 4;
        service.Save();

        var reloaded = new SportsRecordsService(path);
        Assert.Equal(1, reloaded.Current.For(SportKind.Tennis).Wins);
        Assert.Equal(1, reloaded.Current.For(SportKind.Tennis).Played);
        Assert.Equal(TrainingMedal.Silver, reloaded.Current.For(SportKind.Golf).Medal);
        Assert.Equal(4, reloaded.Current.Pal(1).Shirt);

        // A worse training score is not a new best and never lowers the medal.
        var worse = reloaded.Record(new SportResult(SportKind.Golf, SportMode.Training, new[] { 1 }, null, "", "", 1), golf);
        Assert.False(worse.NewBest);
        Assert.Equal(TrainingMedal.Silver, reloaded.Current.For(SportKind.Golf).Medal);
    }

    [Fact]
    public void Two_human_matches_do_not_change_skill()
    {
        using var tmp = new TempDir();
        var service = new SportsRecordsService(Path.Combine(tmp.Path, "sports.json"));
        var setup = new SportSetup(SportKind.Boxing, SportMode.Match, Players: 2, Humans: 2);
        var update = service.Record(new SportResult(SportKind.Boxing, SportMode.Match, new[] { 3, 0 }, 0, "", "", Margin: 3), setup);
        Assert.Equal(update.SkillBefore, update.SkillAfter);
    }
}

public class SwingAndChannelTests
{
    [Fact]
    public void Swing_detector_ignores_rest_and_reports_one_swing_per_motion()
    {
        var detector = new SwingDetector();
        var t = 0.0;
        (float Strength, int Side)? Feed(int x, int y, int z)
        {
            t += 0.01;
            return detector.Feed(x, y, z, t);
        }

        for (var i = 0; i < 50; i++) Assert.Null(Feed(128, 128, 154));

        var swings = new List<(float Strength, int Side)>();
        int[] spike = { 170, 210, 240, 250, 230, 190, 150, 132 };
        foreach (var x in spike)
            if (Feed(x, 128, 154) is { } s) swings.Add(s);
        for (var i = 0; i < 30; i++)
            if (Feed(128, 128, 154) is { } s) swings.Add(s);

        Assert.Single(swings);
        Assert.InRange(swings[0].Strength, 0.3f, 1f);
        Assert.Equal(1, swings[0].Side);
    }

    [Fact]
    public void Wiimote_parser_reads_accelerometer_bytes()
    {
        var report = new byte[22];
        report[0] = 0x33;
        report[3] = 140;
        report[4] = 120;
        report[5] = 154;
        for (var i = 6; i < 18; i++) report[i] = 0xFF;
        var input = WiimoteReportParser.Parse(report)!;
        Assert.Equal((140, 120, 154), input.Accel);

        var status = new byte[22];
        status[0] = 0x20;
        Assert.Null(WiimoteReportParser.Parse(status)!.Accel);
    }

    [Fact]
    public void Existing_layouts_get_the_sports_channel_exactly_once()
    {
        var layout = new ChannelLayout { Seeded = true };
        foreach (var id in BuiltInChannels.Original) LayoutEditor.Place(layout, BuiltInChannels.Create(id));

        Assert.Equal(2, LayoutEditor.OfferNewBuiltIns(layout));
        Assert.NotNull(LayoutEditor.Find(layout, "builtin-" + BuiltInChannels.Sports));
        Assert.NotNull(LayoutEditor.Find(layout, "builtin-" + BuiltInChannels.Pals));

        LayoutEditor.Remove(layout, "builtin-" + BuiltInChannels.Sports);
        Assert.Equal(0, LayoutEditor.OfferNewBuiltIns(layout));
        Assert.Null(LayoutEditor.Find(layout, "builtin-" + BuiltInChannels.Sports));

        // A legacy layout where the user had removed Files must not get Files back.
        var legacy = new ChannelLayout { Seeded = true };
        LayoutEditor.Place(legacy, BuiltInChannels.Create(BuiltInChannels.Photos));
        LayoutEditor.OfferNewBuiltIns(legacy);
        Assert.Null(LayoutEditor.Find(legacy, "builtin-" + BuiltInChannels.Files));
        Assert.NotNull(LayoutEditor.Find(legacy, "builtin-" + BuiltInChannels.Sports));
    }

    [Fact]
    public void First_run_knows_every_built_in()
    {
        var layout = new ChannelLayout();
        var result = ChannelSeeder.Apply(layout, Array.Empty<Couchtop.Core.Discovery.DiscoveredApp>(), autoAddNewApps: true);
        Assert.True(result.FirstRun);
        Assert.Equal(BuiltInChannels.All, layout.KnownBuiltIns);
        Assert.Equal(0, LayoutEditor.OfferNewBuiltIns(layout));
    }
}

public class SportsSimulationTests
{
    /// <summary>Plays a whole game with every player computer-controlled and checks it ends sensibly.</summary>
    private static SportSim PlayOut(SportSetup setup, double maxMinutes)
    {
        var sim = SportSim.Create(setup);
        var commands = new PlayerCommand[Math.Max(1, setup.Players)];
        var limit = maxMinutes * 60 * 60;
        for (var frame = 0; frame < limit && !sim.IsFinished; frame++)
        {
            sim.Update(1.0 / 60, commands);
            if (frame % 30 == 0) sim.DrainEvents();
            Assert.False(string.IsNullOrEmpty(sim.Scoreline));
        }
        Assert.True(sim.IsFinished, $"{setup.Sport} {setup.Mode} did not finish in {maxMinutes} simulated minutes. {sim.Scoreline}");
        return sim;
    }

    [Theory]
    [InlineData(11)]
    [InlineData(29)]
    public void Tennis_match_completes(int seed)
    {
        var sim = (TennisSim)PlayOut(new SportSetup(SportKind.Tennis, SportMode.Match, 2, 0, Difficulty: 2, Seed: seed), 45);
        Assert.NotNull(sim.Result!.Winner);
        Assert.Equal(2, sim.Result.Scores.Max());
        Assert.True(sim.Ball.IsFinite);
    }

    [Theory]
    [InlineData(SportKind.Tennis)]
    [InlineData(SportKind.Baseball)]
    [InlineData(SportKind.Bowling)]
    [InlineData(SportKind.Golf)]
    [InlineData(SportKind.Boxing)]
    public void Training_completes_with_a_score(SportKind sport)
    {
        var sim = PlayOut(new SportSetup(sport, SportMode.Training, 1, 0, Difficulty: 2, Seed: 5), 10);
        Assert.Equal(SportMode.Training, sim.Result!.Mode);
        Assert.NotNull(sim.Result.TrainingScore);
        Assert.True(sim.Result.TrainingScore >= 0);
    }

    [Fact]
    public void Baseball_game_completes()
    {
        var sim = (BaseballSim)PlayOut(new SportSetup(SportKind.Baseball, SportMode.Match, 2, 0, Difficulty: 1, Seed: 3), 60);
        Assert.Equal(sim.Runs.ToArray(), sim.Result!.Scores);
        Assert.True(sim.Inning >= BaseballSim.Innings);
    }

    [Fact]
    public void Bowling_four_players_complete_valid_games()
    {
        var sim = (BowlingSim)PlayOut(new SportSetup(SportKind.Bowling, SportMode.Match, 4, 0, Difficulty: 2, Seed: 9), 60);
        Assert.All(sim.Scorers, s => Assert.True(s.IsComplete));
        Assert.All(sim.Result!.Scores, t => Assert.InRange(t, 0, 300));
        Assert.True(sim.Result.Scores.Max() > 0, "The computer never knocked down a single pin.");
    }

    [Fact]
    public void Golf_round_completes_for_three_players()
    {
        var sim = (GolfSim)PlayOut(new SportSetup(SportKind.Golf, SportMode.Match, 3, 0, Difficulty: 2, Seed: 4), 60);
        Assert.All(sim.Result!.Scores, t => Assert.InRange(t, GolfSim.Course.Count, GolfSim.Course.Count * (GolfSim.MaxStrokes + 1)));
    }

    [Fact]
    public void Boxing_fight_completes()
    {
        var sim = (BoxingSim)PlayOut(new SportSetup(SportKind.Boxing, SportMode.Match, 2, 0, Difficulty: 1, Seed: 7), 10);
        Assert.NotNull(sim.Result);
        Assert.Contains(sim.Fighters, f => f.Hits > 0);
    }

    [Fact]
    public void Golf_terrain_lookup_and_tee_shot_on_first_hole()
    {
        var hole = GolfSim.Course[0];
        Assert.Equal(GolfSim.Terrain.Green, GolfSim.TerrainAt(hole, hole.Pin.X, hole.Pin.Z));
        Assert.Equal(GolfSim.Terrain.Tee, GolfSim.TerrainAt(hole, 0, 0));
        Assert.Equal(GolfSim.Terrain.OutOfBounds, GolfSim.TerrainAt(hole, 500, 50));
        Assert.Equal(GolfSim.Terrain.Water, GolfSim.TerrainAt(GolfSim.Course[2], 0, 58));
    }

    [Fact]
    public void Human_input_drives_bowling_release()
    {
        var sim = new BowlingSim(new SportSetup(SportKind.Bowling, SportMode.Match, 1, 1, Seed: 1));
        var idle = new PlayerCommand[1];
        for (var i = 0; i < 20; i++) sim.Update(1.0 / 60, idle);
        Assert.Equal(BowlingSim.BowlingPhase.Aim, sim.Phase);

        sim.Update(1.0 / 60, new[] { new PlayerCommand { Swing = 0.8f } });
        Assert.Equal(BowlingSim.BowlingPhase.Rolling, sim.Phase);
        for (var i = 0; i < 600 && sim.Phase != BowlingSim.BowlingPhase.Aim; i++) sim.Update(1.0 / 60, idle);
        Assert.Single(sim.Scorers[0].Rolls);
    }
}
