using Avalonia.Input;
using AVAPunchOut.Engine;

namespace AVAPunchOut.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapunchout-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    private static World Fresh(int bout = 0, int lap = 1)
    {
        var w = new World();
        Sim.Start(w, bout, lap);
        return w;
    }

    /// <summary>A fight under way, him standing on guard where he's put (high unless told), throwing nothing of his own.</summary>
    private static World Ready(int bout = 0, bool high = true)
    {
        var w = Fresh(bout);
        w.Him.Still = true;
        w.Him.GuardHigh = high;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static int Steps(double seconds) => (int)Math.Ceiling(seconds / Field.Step);

    /// <summary>Time until his glove arrives.</summary>
    private static double ToImpact(Opponent h) => h.S == Stance.Tell ? h.Dur - h.T + h.Move!.Swing : h.Dur - h.T;

    private static bool Throwing(World w) => w.Him.S is Stance.Tell or Stance.Strike && w.Downed == Who.None;

    /// <summary>Through his punch: dodging each glove a quarter second before it lands, the way <paramref name="side"/> says.</summary>
    private static void DodgeAll(World w, Func<Opponent, int> side)
    {
        for (int i = 0; i < 600 && Throwing(w); i++)
            Sim.Step(w, ToImpact(w.Him) <= 0.25 && w.You.Ready ? new Controls(Dx: side(w.Him)) : default);
    }

    private static void BlockAll(World w)
    {
        for (int i = 0; i < 600 && Throwing(w); i++) Sim.Step(w, new Controls(Dy: 1));
    }

    private static void TakeIt(World w)
    {
        for (int i = 0; i < 600 && Throwing(w); i++) Sim.Step(w, default);
    }

    private static Move MoveOf(World w, string name) => w.P.Moves.First(m => m.Name == name);

    // ───────────────────────────── your punches ─────────────────────────────

    [Fact]
    public void A_body_punch_under_a_high_guard_lands_and_a_punch_to_the_face_into_it_is_blocked()
    {
        var w = Ready(high: true);
        double hp = w.Him.Hp;
        Sim.Step(w, new Controls(Right: true));
        Run(w, 20);
        Assert.Equal(hp - Sim.BodyDamage, w.Him.Hp);
        Assert.Equal(Sim.BodyPoints, w.Score);
        Assert.Equal(1, w.You.Power);
        Assert.Equal(1, w.Landed);

        w = Ready(high: true);
        Sim.Step(w, new Controls(Dy: -1, Left: true));
        Run(w, 20);
        Assert.Equal(hp, w.Him.Hp);
        Assert.Contains(Sfx.Block, w.Heard);
        Assert.Equal(0, w.You.Power);
        Assert.Equal(0, w.Score);
    }

    [Fact]
    public void Up_makes_a_punch_high_and_a_punch_to_the_face_hurts_more()
    {
        var w = Ready(high: false);
        double hp = w.Him.Hp;
        Sim.Step(w, new Controls(Dy: -1, Left: true));
        Assert.True(w.You.High);
        Assert.Equal(-1, w.You.Side);
        Run(w, 20);
        Assert.Equal(hp - Sim.FaceDamage, w.Him.Hp);
        Assert.Equal(Sim.FacePoints, w.Score);
        Assert.Contains(Sfx.HitFace, w.Heard);
    }

    [Fact]
    public void Hit_he_guards_where_it_came_from()
    {
        var w = Ready(high: true);
        Sim.Step(w, new Controls(Right: true));
        Run(w, 30);
        Assert.Equal(Stance.Guard, w.Him.S);
        Assert.False(w.Him.GuardHigh);
    }

    [Fact]
    public void A_good_boxer_reads_your_punch_and_moves_his_guard_to_it()
    {
        var w = Fresh(5);
        w.Him.P = w.Him.P with { Skill = 1, Switch = 0 };
        w.Him.Think = 99;
        w.Him.GuardHigh = true;
        double hp = w.Him.Hp;
        Sim.Step(w, new Controls(Right: true));
        Run(w, 20);
        Assert.Equal(hp, w.Him.Hp);
        Assert.False(w.Him.GuardHigh);
    }

    [Fact]
    public void Too_many_punches_into_his_gloves_and_he_jabs_straight_back()
    {
        var w = Fresh(0);
        w.Him.P = w.Him.P with { Skill = 0, Switch = 0 };
        w.Him.Think = 99;
        w.Him.GuardHigh = true;
        for (int i = 0; i < w.P.Patience; i++)
        {
            Sim.Step(w, new Controls(Dy: -1, Left: true));
            Run(w, Steps(Sim.PunchDur + Sim.Recoil) + 1, new Controls(Dy: -1));
        }
        Assert.True(w.Him.S is Stance.Tell or Stance.Strike);
        Assert.Equal("COUNTER JAB", w.Him.Move!.Name);
        Assert.False(w.Him.Move.Interrupt);
        Assert.True(w.Him.Move.Tell < w.P.Moves[0].Tell);
    }

    [Fact]
    public void A_press_while_you_are_busy_is_kept_for_a_moment()
    {
        var w = Ready(high: true);
        Sim.Step(w, new Controls(Left: true));
        Run(w, Steps(Sim.PunchDur) - 6);
        Sim.Step(w, new Controls(Right: true));
        Run(w, 8);
        Assert.Equal(Act.Punch, w.You.Act);
        Assert.Equal(1, w.You.Side);
        Assert.Equal(2, w.Thrown);
    }

    // ───────────────────────────── his punches, and stopping them ─────────────────────────────

    [Fact]
    public void What_stops_each_kind_of_punch()
    {
        // Jabs and body blows: a block, or a dodge either way.
        foreach (var k in new[] { Kind.Jab, Kind.Body })
        {
            Assert.True(Move.Stops(k, 0, true, 0));
            Assert.True(Move.Stops(k, 0, false, -1));
            Assert.True(Move.Stops(k, 0, false, 1));
            Assert.False(Move.Stops(k, 0, false, 0));
        }
        // A hook: a block, or a dodge away from the glove (into it, it lands).
        Assert.True(Move.Stops(Kind.Hook, -1, true, 0));
        Assert.True(Move.Stops(Kind.Hook, -1, false, 1));
        Assert.False(Move.Stops(Kind.Hook, -1, false, -1));
        Assert.True(Move.Stops(Kind.Hook, 1, false, -1));
        // An uppercut: only a dodge, either way.
        Assert.False(Move.Stops(Kind.Upper, 0, true, 0));
        Assert.True(Move.Stops(Kind.Upper, 0, false, -1) && Move.Stops(Kind.Upper, 0, false, 1));
        // A haymaker: only a dodge away from it.
        Assert.False(Move.Stops(Kind.Haymaker, 1, true, 0));
        Assert.True(Move.Stops(Kind.Haymaker, 1, false, -1));
        Assert.False(Move.Stops(Kind.Haymaker, 1, false, 1));
        // A smash: only a block.
        Assert.True(Move.Stops(Kind.Smash, 0, true, 0));
        Assert.False(Move.Stops(Kind.Smash, 0, false, -1) || Move.Stops(Kind.Smash, 0, false, 1));
    }

    [Fact]
    public void A_dodged_punch_leaves_him_open_and_a_counter_does_half_as_much_again()
    {
        var w = Ready(0);
        Sim.Throw(w, w.P.Moves[0]);
        DodgeAll(w, _ => 1);
        Assert.Equal(Sim.Stamina, w.You.Hp);
        Assert.Equal(Stance.Open, w.Him.S);
        Assert.Contains(Sfx.Whiff, w.Heard);
        double hp = w.Him.Hp;
        Run(w, 15);
        Assert.True(w.You.Ready);
        Sim.Step(w, new Controls(Dy: -1, Left: true));
        Run(w, 10);
        Assert.Equal(hp - Sim.FaceDamage * Sim.CounterFactor, w.Him.Hp);
        Assert.Equal(1, w.Counters);
        Assert.Equal(2, w.You.Power);
        Assert.Equal(Sim.FacePoints * 2, w.Score);
        Assert.Equal("COUNTER!", w.Call);
    }

    [Fact]
    public void A_blocked_punch_does_no_harm_but_leaves_no_opening()
    {
        var w = Ready(0);
        Sim.Throw(w, w.P.Moves[1]);
        BlockAll(w);
        Assert.Equal(Sim.Stamina, w.You.Hp);
        Assert.Equal(Stance.Recover, w.Him.S);
        Assert.Contains(Sfx.Block, w.Heard);
    }

    [Fact]
    public void A_punch_that_lands_hurts_rocks_you_and_empties_the_power_meter()
    {
        var w = Ready(0);
        w.You.Power = 6;
        var jab = w.P.Moves[0];
        Sim.Throw(w, jab);
        TakeIt(w);
        Assert.Equal(Sim.Stamina - jab.Damage, w.You.Hp);
        Assert.Equal(Act.Hurt, w.You.Act);
        Assert.Equal(0, w.You.Power);
        Assert.Equal(Stance.Recover, w.Him.S);
        Assert.Equal(1, w.Points[1]);
    }

    [Fact]
    public void Dodge_into_a_hook_and_it_lands()
    {
        var w = Ready(0);
        var hook = w.P.Moves.First(m => m is { Kind: Kind.Hook, Side: -1 });
        Sim.Throw(w, hook);
        DodgeAll(w, _ => -1);
        Assert.Equal(Sim.Stamina - hook.Damage, w.You.Hp);
        w = Ready(0);
        Sim.Throw(w, hook);
        DodgeAll(w, _ => 1);
        Assert.Equal(Sim.Stamina, w.You.Hp);
        Assert.Equal(Stance.Open, w.Him.S);
    }

    [Fact]
    public void Big_Biscuits_gravy_train_can_only_be_blocked_and_blocking_it_opens_him_up()
    {
        var w = Ready(4);
        var smash = MoveOf(w, "GRAVY TRAIN");
        Sim.Throw(w, smash);
        DodgeAll(w, _ => 1);
        Assert.Equal(Sim.Stamina - smash.Damage, w.You.Hp);
        w = Ready(4);
        Sim.Throw(w, smash);
        BlockAll(w);
        Assert.Equal(Sim.Stamina, w.You.Hp);
        Assert.Equal(Stance.Open, w.Him.S);
    }

    [Fact]
    public void An_uppercut_goes_through_a_block()
    {
        var w = Ready(1);
        var flurry = MoveOf(w, "ROCKET FLURRY");
        Sim.Throw(w, flurry);
        BlockAll(w);
        Assert.True(w.You.Hp < Sim.Stamina);
    }

    [Fact]
    public void The_tell_lasts_his_wind_up_then_the_glove_flies_then_lands()
    {
        var w = Ready(0);
        var jab = w.P.Moves[0];
        Sim.Throw(w, jab);
        Assert.Equal(Stance.Tell, w.Him.S);
        Run(w, Steps(jab.Tell) - 2);
        Assert.Equal(Stance.Tell, w.Him.S);
        Run(w, 3);
        Assert.Equal(Stance.Strike, w.Him.S);
        Assert.Equal(Sim.Stamina, w.You.Hp);
        Run(w, Steps(jab.Swing) + 1);
        Assert.True(w.You.Hp < Sim.Stamina);
    }

    [Fact]
    public void A_special_punch_rings_a_warning_as_he_winds_it_up()
    {
        var w = Ready(2);
        Sim.Throw(w, MoveOf(w, "AVALANCHE"));
        Assert.Contains(Sfx.Tell, w.Heard);
        w = Ready(2);
        Sim.Throw(w, w.P.Moves[0]);
        Assert.DoesNotContain(Sfx.Tell, w.Heard);
    }

    [Fact]
    public void A_flurry_must_be_dodged_hit_by_hit_and_only_a_clean_one_opens_him()
    {
        var w = Ready(1);
        var flurry = MoveOf(w, "ROCKET FLURRY");
        Assert.Equal(3, flurry.Hits);
        Sim.Throw(w, flurry);
        DodgeAll(w, _ => -1);
        Assert.Equal(Sim.Stamina, w.You.Hp);
        Assert.Equal(Stance.Open, w.Him.S);
        w = Ready(1);
        Sim.Throw(w, flurry);
        TakeIt(w);
        Assert.Equal(Stance.Recover, w.Him.S);
        Assert.Equal(3, w.Points[1]);
    }

    [Fact]
    public void Twin_hooks_come_from_one_glove_then_the_other()
    {
        var w = Ready(3);
        var twin = w.P.Moves.First(m => m.Name == "TWIN HOOKS" && m.Side == -1);
        Sim.Throw(w, twin);
        DodgeAll(w, h => -h.Side);
        Assert.Equal(Sim.Stamina, w.You.Hp);
        Assert.Equal(Stance.Open, w.Him.S);
        w = Ready(3);
        Sim.Throw(w, twin);
        DodgeAll(w, _ => 1);
        Assert.Equal(Sim.Stamina - twin.Damage, w.You.Hp);
    }

    [Fact]
    public void An_early_opponents_wind_up_can_be_beaten_to_but_not_the_champions()
    {
        var w = Ready(0);
        var jab = w.P.Moves[0];
        Assert.True(jab.Interrupt);
        double hp = w.Him.Hp;
        Sim.Throw(w, jab);
        Run(w, 10);
        Sim.Step(w, new Controls(Dy: -1, Right: true));
        Run(w, 8);
        Assert.Equal(Stance.Hurt, w.Him.S);
        Assert.Null(w.Him.Move);
        Assert.Equal(hp - Sim.FaceDamage * Sim.CounterFactor, w.Him.Hp);
        Run(w, 120);
        Assert.Equal(Sim.Stamina, w.You.Hp);

        w = Ready(5);
        hp = w.Him.Hp;
        Assert.False(w.P.Moves[0].Interrupt);
        Sim.Throw(w, w.P.Moves[0]);
        Sim.Step(w, new Controls(Dy: -1, Right: true));
        Run(w, 8);
        Assert.Equal(hp, w.Him.Hp);
        Assert.Contains(Sfx.Block, w.Heard);
    }

    [Fact]
    public void A_body_punch_late_in_gunnars_avalanche_breaks_it_and_drops_him_but_not_after_a_wasted_punch()
    {
        var w = Ready(2);
        var avalanche = MoveOf(w, "AVALANCHE");
        Assert.True(avalanche.Breaker);
        Sim.Throw(w, avalanche);
        Run(w, Steps(avalanche.Tell * 0.65));
        Sim.Step(w, new Controls(Left: true));
        Run(w, 8);
        Assert.Equal(Who.Him, w.Downed);
        Assert.Equal(1, w.Counters);

        w = Ready(2);
        Sim.Throw(w, avalanche);
        Run(w, 10);
        Sim.Step(w, new Controls(Left: true));
        Run(w, Steps(avalanche.Tell * 0.65) - 10);
        Sim.Step(w, new Controls(Right: true));
        Run(w, 8);
        Assert.Equal(Who.None, w.Downed);
        Assert.Equal(w.Him.MaxHp, w.Him.Hp);
    }

    [Fact]
    public void A_taunt_leaves_him_open_to_a_counter()
    {
        var w = Ready(0);
        w.Him.S = Stance.Taunt;
        w.Him.T = 0;
        w.Him.Dur = Sim.TauntTime;
        Sim.Step(w, new Controls(Dy: -1, Left: true));
        Run(w, 10);
        Assert.Equal(1, w.Counters);
        Assert.Equal(Stance.Open, w.Him.S);
    }

    [Fact]
    public void Too_many_in_a_row_and_he_covers_up_and_a_cover_up_stops_anything()
    {
        var w = Ready(0);
        w.Him.S = Stance.Open;
        w.Him.T = 0;
        w.Him.Dur = 30;
        for (int i = 0; i < w.P.Cover + 2; i++)
        {
            Sim.Step(w, new Controls(Dy: -1, Left: true));
            Run(w, Steps(Sim.PunchDur) + 1);
        }
        Assert.Equal(Stance.Cover, w.Him.S);
        double hp = w.Him.Hp;
        w.You.Power = Sim.PowerMax;
        Sim.Step(w, new Controls(Ko: true));
        Run(w, Steps(Sim.KoHit) + 1);
        Assert.Equal(hp, w.Him.Hp);
        Assert.Contains(Sfx.Block, w.Heard);
    }

    // ───────────────────────────── the power meter and the KO punch ─────────────────────────────

    [Fact]
    public void The_meter_fills_a_point_a_punch_two_a_counter_and_chimes_when_full()
    {
        var w = Ready(high: true);
        w.You.Power = Sim.PowerMax - 2;
        Sim.Step(w, new Controls(Left: true));
        Run(w, 20);
        Assert.Equal(Sim.PowerMax - 1, w.You.Power);
        Assert.DoesNotContain(Sfx.Star, w.Heard);
        w.Him.S = Stance.Open;
        w.Him.T = 0;
        w.Him.Dur = 5;
        Sim.Step(w, new Controls(Dy: -1, Right: true));
        Run(w, 20);
        Assert.Equal(Sim.PowerMax, w.You.Power);
        Assert.Contains(Sfx.Star, w.Heard);
    }

    [Fact]
    public void The_KO_punch_needs_a_full_meter_goes_through_his_guard_and_hits_hard()
    {
        var w = Ready(5, high: true);
        w.You.Power = Sim.PowerMax - 1;
        Sim.Step(w, new Controls(Ko: true));
        Assert.Equal(Act.Idle, w.You.Act);
        Assert.Equal(Sim.PowerMax - 1, w.You.Power);

        w.You.Power = Sim.PowerMax;
        double hp = w.Him.Hp;
        Sim.Step(w, new Controls(Ko: true));
        Assert.Equal(Act.KoPunch, w.You.Act);
        Assert.Equal(0, w.You.Power);
        Assert.Contains(Sfx.KoPunch, w.Heard);
        Run(w, Steps(Sim.KoHit) + 1);
        Assert.Equal(hp - Sim.KoDamage, w.Him.Hp);
        Assert.Equal(Stance.Open, w.Him.S);
        Assert.Equal(Sim.KoPunchPoints, w.Score);
    }

    // ───────────────────────────── knockdowns and the count ─────────────────────────────

    private static void Drop(World w)
    {
        w.Him.Hp = 1;
        w.Him.S = Stance.Guard;
        w.Him.GuardHigh = true;
        Sim.Step(w, new Controls(Left: true));
        Run(w, 8);
    }

    [Fact]
    public void Down_he_goes_the_referee_counts_a_number_a_second_and_he_gets_up_at_his_count()
    {
        var w = Ready(0);
        Drop(w);
        Assert.Equal(Who.Him, w.Downed);
        Assert.Equal(Stance.Down, w.Him.S);
        Assert.Equal(1, w.RoundDowns[1]);
        Assert.Contains(Sfx.Fall, w.Heard);
        Assert.True(w.Score >= Sim.KnockdownPoints);
        Assert.Equal(5 + 1, w.Points[0]);
        double clock = w.Clock;
        Run(w, Steps(Sim.CountSecond));
        Assert.Equal(1, w.Count);
        Assert.Equal(clock, w.Clock);
        Assert.Contains(Sfx.SayOne, w.Heard);
        int rise = w.P.Rises[0];
        Run(w, Steps(Sim.CountSecond * (rise - 1)));
        Assert.Equal(Who.None, w.Downed);
        Assert.Equal(Stance.Rise, w.Him.S);
        Assert.Equal(rise, w.Count);
        Assert.Equal(w.Him.MaxHp * Sim.HisRiseShare(1), w.Him.Hp, 6);
        Assert.DoesNotContain(Sfx.SayKnockOut, w.Heard);
    }

    [Fact]
    public void Down_and_not_getting_up_he_is_counted_out()
    {
        var w = Ready(0);
        w.Him.P = w.Him.P with { Rises = [10] };
        Drop(w);
        Run(w, Steps(Sim.CountSecond * 9.5));
        Assert.Equal(Outcome.None, w.Outcome);
        Assert.Equal(9, w.Count);
        Run(w, Steps(Sim.CountSecond));
        Assert.Equal(10, w.Count);
        Assert.Equal(Outcome.WonKo, w.Outcome);
        Assert.Equal(Stance.Out, w.Him.S);
        Assert.Equal(Act.Win, w.You.Act);
        Assert.Contains(Sfx.SayTen, w.Heard);
        Assert.Contains(Sfx.SayKnockOut, w.Heard);
    }

    [Fact]
    public void Three_knockdowns_in_a_round_is_a_TKO()
    {
        var w = Ready(5);
        w.Him.P = w.Him.P with { Rises = [1, 1, 1] };
        for (int i = 0; i < 2; i++)
        {
            Drop(w);
            Assert.Equal(Who.Him, w.Downed);
            Run(w, Steps(Sim.CountSecond * 1.5 + Sim.RiseTime) + 2);
            Assert.Equal(Who.None, w.Downed);
            Assert.Equal(Outcome.None, w.Outcome);
        }
        Drop(w);
        Assert.Equal(Outcome.WonTko, w.Outcome);
        Assert.Equal(3, w.RoundDowns[1]);
    }

    [Fact]
    public void Down_yourself_you_get_up_by_pressing_and_lie_there_you_are_counted_out()
    {
        var w = Ready(0);
        w.You.Hp = 1;
        Sim.Throw(w, w.P.Moves[0]);
        TakeIt(w);
        Assert.Equal(Who.You, w.Downed);
        Assert.Equal(Act.Down, w.You.Act);
        Assert.Equal(Stance.Corner, w.Him.S);
        int need = Sim.RiseNeed(1);
        for (int i = 0; i < need; i++) { Sim.Step(w, new Controls(Left: i % 2 == 0, Right: i % 2 == 1)); Run(w, 3); }
        Assert.Equal(Who.You, w.Downed);
        Run(w, Steps(Sim.CountSecond * 2));
        Assert.Equal(Who.None, w.Downed);
        Assert.Equal(Act.Idle, w.You.Act);
        Assert.Equal(Sim.Stamina * Sim.RiseShare(1), w.You.Hp, 6);
        Assert.Equal(2, w.Count);

        w = Ready(0);
        w.You.Hp = 1;
        Sim.Throw(w, w.P.Moves[0]);
        TakeIt(w);
        Run(w, Steps(Sim.CountSecond * 10) + 1);
        Assert.Equal(Outcome.LostKo, w.Outcome);
        Assert.Equal(Act.Lose, w.You.Act);
        Assert.Equal(Stance.Cheer, w.Him.S);
    }

    [Fact]
    public void Getting_up_takes_more_presses_each_time()
    {
        Assert.True(Sim.RiseNeed(2) > Sim.RiseNeed(1));
        Assert.True(Sim.RiseShare(2) < Sim.RiseShare(1));
        Assert.True(Sim.HisRiseShare(2) < Sim.HisRiseShare(1));
    }

    // ───────────────────────────── the clock, rounds and decisions ─────────────────────────────

    [Fact]
    public void A_three_minute_round_on_a_clock_three_times_as_fast_as_real_time()
    {
        var w = Ready(0);
        Run(w, Steps(Sim.RoundSeconds / Sim.ClockRate) - 3);
        Assert.False(w.RoundOver);
        Assert.InRange(w.Clock, 179, 180);
        Run(w, 4);
        Assert.True(w.RoundOver);
        Assert.Equal(Outcome.None, w.Outcome);
        Assert.Contains(Sfx.Bell, w.Heard);
        w.You.Hp = 50;
        Sim.NextRound(w);
        Assert.Equal(2, w.Round);
        Assert.Equal(0, w.Clock);
        Assert.False(w.RoundOver);
        Assert.Equal(50 + 0.3 * Sim.Stamina, w.You.Hp, 6);
    }

    [Fact]
    public void After_the_last_round_the_judges_decide_on_points()
    {
        foreach (var (mine, his, expect) in new[] { (12, 7, Outcome.WonDecision), (7, 12, Outcome.LostDecision), (9, 9, Outcome.Draw) })
        {
            var w = Ready(0);
            w.Round = Sim.Rounds;
            w.Points = [mine, his];
            Run(w, Steps(Sim.RoundSeconds / Sim.ClockRate) + 2);
            Assert.Equal(expect, w.Outcome);
            Assert.Equal(expect == Outcome.WonDecision, w.Outcome.Won());
        }
    }

    // ───────────────────────────── the circuit ─────────────────────────────

    [Fact]
    public void Six_opponents_each_tougher_than_the_last_each_with_a_special_and_his_own_tell()
    {
        var b = Circuit.Boxers;
        Assert.Equal(6, b.Length);
        for (int i = 1; i < b.Length; i++) Assert.True(b[i].Hp > b[i - 1].Hp, b[i].Name);
        Assert.All(b, p => Assert.Contains(p.Moves, m => m.Special));
        Assert.All(b, p => Assert.All(p.Pattern, i => Assert.InRange(i, 0, p.Moves.Length - 1)));
        Assert.Equal(6, b.Select(p => p.Look.Tell).Distinct().Count());
        Assert.Equal(6, b.Select(p => p.Name).Distinct().Count());
        Assert.True(b[^1].Moves.Min(m => m.Tell) < b[0].Moves.Min(m => m.Tell));
    }

    [Fact]
    public void Round_the_circuit_again_they_are_quicker_harder_tougher_and_up_sooner()
    {
        var one = Circuit.Get(2, 1);
        var two = Circuit.Get(2, 2);
        Assert.Equal(0.8, two.Pace, 6);
        Assert.True(two.Power > one.Power);
        Assert.True(two.Hp > one.Hp);
        Assert.True(two.Skill > one.Skill);
        Assert.True(two.Rises[0] < one.Rises[0]);
        Assert.Equal(Circuit.Get(0).Name, Circuit.Get(6).Name);
        var w = Ready(2);
        var w2 = new World();
        Sim.Start(w2, 2, 2);
        var jab = w.P.Moves[0];
        Assert.True(w2.Him.TellTime(jab, 0) < w.Him.TellTime(jab, 0));
    }

    // ───────────────────────────── the game ─────────────────────────────

    /// <summary>Knocks him out at once: one punch drops him and he stays down.</summary>
    private static void KnockOut(GameEngine g)
    {
        var h = g.World.Him;
        h.P = h.P with { Rises = [10] };
        h.Still = true;
        h.S = Stance.Guard;
        h.GuardHigh = true;
        h.Hp = 1;
        g.Tick(new Controls(Left: true));
        for (int i = 0; i < 60 * 15 && g.Phase != Phase.Result; i++) g.Tick(default);
        Assert.Equal(Phase.Result, g.Phase);
    }

    [Fact]
    public void A_knockout_wins_a_bonus_for_the_knockout_and_the_time_left_then_on_to_the_next_opponent()
    {
        var g = Engine();
        g.Go(1);
        KnockOut(g);
        Assert.Equal(Outcome.WonKo, g.World.Outcome);
        Assert.Equal(GameEngine.KoBonus * 2, g.FinishBonus);
        Assert.True(g.TimeBonus > 0);
        int score = g.Score;
        Assert.True(score >= g.FinishBonus + g.TimeBonus + Sim.KnockdownPoints);
        g.Run(Steps(GameEngine.ResultSeconds) + 2);
        Assert.Equal(Phase.Intro, g.Phase);
        Assert.Equal(2, g.Bout);
        Assert.Equal(1, g.Wins);
        Assert.Equal("GRANITE GUNNAR", g.Opponent.Name);
        Assert.Equal(score, g.Score);
    }

    [Fact]
    public void After_the_champion_the_circuit_starts_again_harder()
    {
        var g = Engine();
        g.Go(Circuit.Count - 1);
        KnockOut(g);
        g.Run(Steps(GameEngine.ResultSeconds) + 2);
        Assert.Equal(0, g.Bout);
        Assert.Equal(2, g.Lap);
        Assert.Equal(2, g.World.Lap);
        Assert.Equal(0.8, g.Opponent.Pace, 6);
    }

    [Fact]
    public void Lose_and_you_get_a_rematch_lose_twice_and_it_is_over_then_a_high_score_is_entered()
    {
        var g = Engine();
        g.Go(2);
        Assert.Equal(GameEngine.Lives0, g.Lives);
        for (int round = 0; round < 2; round++)
        {
            g.World.You.Hp = 1;
            for (int i = 0; i < 60 * 60 && g.Phase != Phase.Result; i++) g.Tick(default);
            Assert.Equal(Outcome.LostKo, g.World.Outcome);
            g.World.Score = 50000;
            g.Run(Steps(GameEngine.ResultSeconds) + 2);
            if (round == 0)
            {
                Assert.Equal(GameState.Playing, g.State);
                Assert.Equal(Phase.Intro, g.Phase);
                Assert.Equal(2, g.Bout);
                Assert.Equal(1, g.Lives);
                g.Run(Steps(GameEngine.IntroSeconds + GameEngine.BellSeconds) + 4);
                Assert.Equal(Phase.Fight, g.Phase);
            }
        }
        Assert.Equal(GameState.GameOver, g.State);
        Assert.Equal(2, g.Losses);
        g.Run(Steps(GameEngine.OverSeconds) + 2);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('P');
        g.Initials.Type('O');
        g.Initials.Type('W');
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("POW", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void Between_rounds_a_rest_then_the_bell_for_the_next()
    {
        var g = Engine();
        g.Go(0);
        g.World.Him.Still = true;
        g.Run(Steps(Sim.RoundSeconds / Sim.ClockRate) + 2);
        Assert.Equal(Phase.Break, g.Phase);
        g.Run(Steps(GameEngine.BreakSeconds) + 1);
        Assert.Equal(Phase.Bell, g.Phase);
        Assert.Equal(2, g.World.Round);
        g.Run(Steps(GameEngine.BellSeconds) + 1);
        Assert.Equal(Phase.Fight, g.Phase);
    }

    [Fact]
    public void The_card_can_be_skipped_with_a_punch_once_it_has_been_seen_a_moment()
    {
        var g = Engine();
        g.StartGame();
        Assert.Equal(Phase.Intro, g.Phase);
        Assert.Equal(0, g.Bout);
        g.Tick(new Controls(Left: true));
        Assert.Equal(Phase.Intro, g.Phase);
        g.Run(61);
        g.Tick(new Controls(Left: true));
        Assert.Equal(Phase.Bell, g.Phase);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Engine();
        g.Idle(Steps(GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(Steps(GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(Steps(GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Enter_starts_a_game_and_a_punch_on_the_title_does_too()
    {
        var g = Engine();
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        g = Engine();
        g.Idle(40);
        g.Tick(new Controls(Right: true));
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void A_press_is_kept_until_a_step_takes_it_and_taken_only_once()
    {
        var g = Engine();
        g.Go(0);
        g.World.Him.Still = true;
        g.Update(0.005, new Controls(Left: true), false);
        Assert.Equal(Act.Idle, g.World.You.Act);
        g.Update(0.02, default, false);
        Assert.Equal(Act.Punch, g.World.You.Act);
        g.Update(0.5, default, false);
        Assert.Equal(1, g.World.Thrown);
    }

    [Fact]
    public void Every_sound_the_game_asks_for_has_its_file()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.CrowdStop))
            Assert.True(File.Exists(Path.Combine(folder, s + ".wav")), $"no {s}.wav");
        Assert.Equal(Sfx.SayTen, Sfx.SayOne + 9);
    }

    // ───────────────────────────── the keys ─────────────────────────────

    [Fact]
    public void The_keys_up_with_a_punch_is_to_the_face_down_blocks_the_arrows_dodge()
    {
        double now = 0;
        using var input = new InputHandler(() => now);
        input.OnKeyDown(Key.Up);
        input.OnKeyDown(Key.Z);
        var c = input.Read();
        Assert.Equal(-1, c.Dy);
        Assert.True(c.Left);
        // And up means high in the ring too.
        var w = Ready(high: false);
        Sim.Step(w, c);
        Assert.Equal(Act.Punch, w.You.Act);
        Assert.True(w.You.High);
        input.OnKeyUp(Key.Up);
        input.OnKeyUp(Key.Z);
        input.OnKeyDown(Key.Down);
        c = input.Read();
        Assert.Equal(1, c.Dy);
        Assert.False(c.Pressed);
        w = Ready();
        Sim.Step(w, c);
        Assert.Equal(Act.Block, w.You.Act);
        input.OnKeyUp(Key.Down);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Read().Dx);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Read().Dx);
        input.OnKeyDown(Key.X);
        c = input.Read();
        Assert.True(c.Right);
        Assert.Equal(0, c.Dy);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Read().Ko);
    }

    [Fact]
    public void A_held_key_repeating_is_not_another_punch()
    {
        double now = 0;
        using var input = new InputHandler(() => now);
        input.OnKeyDown(Key.Z);
        Assert.True(input.Read().Left);
        // Auto-repeat: more key-downs while it's held.
        input.OnKeyDown(Key.Z);
        input.OnKeyDown(Key.Z);
        Assert.False(input.Read().Left);
        // Auto-repeat sent as a release and a press at the same instant.
        input.OnKeyUp(Key.Z);
        now += 0.004;
        input.OnKeyDown(Key.Z);
        Assert.False(input.Read().Left);
        // A real second press.
        input.OnKeyUp(Key.Z);
        now += 0.15;
        input.OnKeyDown(Key.Z);
        Assert.True(input.Read().Left);
        Assert.False(input.Read().Left);
        // The arrows too: a held arrow is one dodge.
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Read().Dx);
        input.OnKeyDown(Key.Left);
        Assert.Equal(0, input.Read().Dx);
    }

    // ───────────────────────────── the computer boxer ─────────────────────────────

    [Fact]
    public void The_computer_boxer_knocks_out_the_first_opponent_without_a_scratch()
    {
        var g = Engine();
        g.Go(0);
        Assert.True(g.PilotUntil(e => e.Phase == Phase.Result, 300));
        Assert.Equal(Outcome.WonKo, g.World.Outcome);
        Assert.Equal(0, g.World.Downs[0]);
    }

    [Fact]
    public void The_computer_boxer_wins_the_first_circuit_but_the_second_beats_it()
    {
        var g = Engine();
        g.Go(0);
        g.SetLives(1);
        g.PilotUntil(e => e.State != GameState.Playing, 3600);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.True(g.Wins >= Circuit.Count, $"won {g.Wins}");
        Assert.Equal(2, g.Lap);
        Assert.Equal(1, g.Losses);
    }

    [Fact]
    public void Punching_away_without_defending_does_not_get_past_the_third_opponent()
    {
        var g = Engine();
        g.Go(0);
        g.SetLives(1);
        for (int t = 0; t < 60 * 3600 && g.State == GameState.Playing; t++)
            g.Tick(new Controls(Dy: t % 40 < 20 ? -1 : 0, Left: t % 20 == 0, Right: t % 20 == 10));
        Assert.Equal(GameState.GameOver, g.State);
        Assert.True(g.Wins <= 2, $"won {g.Wins}");
    }
}
