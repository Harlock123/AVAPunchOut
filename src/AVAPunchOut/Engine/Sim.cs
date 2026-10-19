namespace AVAPunchOut.Engine;

/// <summary>The rules of the ring, one 60th of a second at a time.</summary>
public static class Sim
{
    /// <summary>Your stamina, full.</summary>
    public const double Stamina = 100;
    /// <summary>A round is three minutes on the clock, which runs three times as fast as real time; three rounds a fight.</summary>
    public const double RoundSeconds = 180, ClockRate = 3;
    public const int Rounds = 3, TkoDowns = 3;
    /// <summary>Your punches: how long, and when in them the glove arrives.</summary>
    public const double PunchDur = 0.26, PunchHit = 0.09, KoDur = 0.62, KoHit = 0.24;
    /// <summary>A dodge: how long, and the part of it you're out of the way.</summary>
    public const double DodgeDur = 0.44, DodgeFrom = 0.02, DodgeTo = 0.40;
    /// <summary>Rocked by a punch; and the extra moment a punch into his guard costs you.</summary>
    public const double HurtDur = 0.42, Recoil = 0.16;
    public const double BodyDamage = 4, FaceDamage = 6, CounterFactor = 1.5, KoDamage = 30;
    /// <summary>The power meter: a point a punch that lands, two a counter; full, the KO punch.</summary>
    public const int PowerMax = 10;
    /// <summary>The referee's count: a number a second.</summary>
    public const double CountSecond = 1.0;
    /// <summary>A press while you're busy is kept this long, for when you're ready.</summary>
    public const double BufferTime = 0.2;
    public const double HurtTime = 0.24, RecoverTime = 0.45, CoverTime = 0.7, TauntTime = 1.4, RiseTime = 1.2, KoStun = 1.0;
    /// <summary>A breaker works from this far into the wind-up, and only if it's the first punch you throw into it.</summary>
    public const double BreakFrom = 0.55;
    /// <summary>A quick jab after too many of your punches into his gloves.</summary>
    public const double CounterJabTell = 0.32;
    public const int BodyPoints = 10, FacePoints = 20, KoPunchPoints = 300, KnockdownPoints = 1000;

    public static void Start(World w, int bout, int lap = 1, int score = 0)
    {
        var p = Circuit.Get(bout, lap);
        w.Bout = bout;
        w.Lap = lap;
        w.You = new Boxer();
        w.Him = new Opponent { P = p, Hp = p.Hp, MaxHp = p.Hp, Think = 1.5 };
        w.Round = 1;
        w.Clock = 0;
        w.RoundOver = false;
        w.Outcome = Outcome.None;
        w.Downed = Who.None;
        w.Count = 0;
        w.CountT = 0;
        w.RoundDowns = new int[2];
        w.Downs = new int[2];
        w.Points = new int[2];
        w.Landed = w.Thrown = w.Counters = 0;
        w.Score = score;
        w.Buffer = default;
        w.BufferT = 0;
        w.Call = "";
        w.CallT = 0;
        w.Shake = 0;
        w.Sparks.Clear();
        w.Dice = new Dice(1984 + bout * 31 + lap * 977);
    }

    /// <summary>Presses to get up from your <paramref name="downs"/>th knockdown of the fight.</summary>
    public static int RiseNeed(int downs) => 10 * downs;

    /// <summary>The share of your stamina you get up with, after your <paramref name="downs"/>th knockdown.</summary>
    public static double RiseShare(int downs) => Math.Max(0.2, 0.6 - 0.15 * (downs - 1));

    /// <summary>The share of his stamina he gets up with, after his <paramref name="downs"/>th knockdown.</summary>
    public static double HisRiseShare(int downs) => Math.Max(0.3, 0.75 - 0.15 * (downs - 1));

    /// <summary>The count he gets up at, this knockdown (10 or more: he doesn't).</summary>
    public static int RiseAt(World w)
    {
        var r = w.P.Rises;
        return r[Math.Clamp(w.Downs[1] - 1, 0, r.Length - 1)];
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        var y = w.You;
        var h = w.Him;
        y.T += dt;
        h.T += dt;
        if (w.CallT > 0) w.CallT -= dt;
        if (w.BufferT > 0) w.BufferT -= dt;
        w.Shake = Math.Max(0, w.Shake - dt * 40);
        for (int i = 0; i < w.Sparks.Count; i++) w.Sparks[i] = w.Sparks[i] with { Age = w.Sparks[i].Age + dt };
        w.Sparks.RemoveAll(s => s.Age > 0.45);
        if (w.Outcome != Outcome.None || w.RoundOver) return;
        if (w.Downed != Who.None)
        {
            Count(w, c);
            return;
        }
        // The clock stops for a count, and the bell ends the round.
        w.Clock += dt * ClockRate;
        if (w.Clock >= RoundSeconds)
        {
            w.Clock = RoundSeconds;
            EndRound(w);
            return;
        }
        You(w, c);
        Him(w);
        YourPunch(w);
    }

    private static void Begin(Boxer y, Act a, double dur)
    {
        y.Act = a;
        y.T = 0;
        y.Dur = dur;
        y.Struck = false;
    }

    private static void Set(Opponent h, Stance s, double dur)
    {
        h.S = s;
        h.T = 0;
        h.Dur = dur;
    }

    private static void Call(World w, string text, double seconds = 0.9)
    {
        w.Call = text;
        w.CallT = seconds;
    }

    // ───────────────────────────── you ─────────────────────────────

    /// <summary>Your next move, from the controls: the KO punch, a punch (high with up held), a dodge, or a block (down held).</summary>
    private static void You(World w, Controls c)
    {
        var y = w.You;
        if (!y.Ready)
        {
            if (c.Pressed)
            {
                w.Buffer = c.Presses;
                w.BufferT = BufferTime;
            }
            return;
        }
        if (!c.Pressed && w.BufferT > 0) c = w.Buffer with { Dy = c.Dy };
        w.BufferT = 0;
        if (c.Ko && y.Power >= PowerMax)
        {
            Begin(y, Act.KoPunch, KoDur);
            y.Side = 1;
            y.High = true;
            y.Power = 0;
            w.Thrown++;
            w.Heard.Add(Sfx.KoPunch);
            return;
        }
        if (c.Left || c.Right)
        {
            Begin(y, Act.Punch, PunchDur);
            y.Side = c.Left ? -1 : 1;
            y.High = c.Dy < 0;
            w.Thrown++;
            w.Heard.Add(Sfx.Swing);
            // He reads it coming, if he's good enough, and moves his guard to it.
            var h = w.Him;
            if (h.S == Stance.Guard && !h.Still && w.Dice.Next() < h.P.Skill) h.GuardHigh = y.High;
            return;
        }
        if (c.Dx != 0)
        {
            Begin(y, Act.Dodge, DodgeDur);
            y.Side = Math.Sign(c.Dx);
            w.Heard.Add(Sfx.Dodge);
            return;
        }
        var act = c.Dy > 0 ? Act.Block : Act.Idle;
        if (y.Act != act) Begin(y, act, 0);
    }

    /// <summary>
    /// Your glove arriving. His guard stops a punch at its height, and a cover-up stops anything. In his wind-up, a
    /// punch beats him to it if the punch he's winding up can be interrupted (a counter); if not, he's guarded, unless
    /// it's a breaker and your first punch into it goes to the body late in it. Off balance or showing off, he takes a
    /// counter: half as much again. The KO punch goes through any guard but a cover-up.
    /// </summary>
    private static void YourPunch(World w)
    {
        var y = w.You;
        var h = w.Him;
        bool ko = y.Act == Act.KoPunch;
        if (y.Act is not (Act.Punch or Act.KoPunch) || y.Struck || y.T < (ko ? KoHit : PunchHit)) return;
        y.Struck = true;
        bool high = y.High, counter = false, breaker = false;
        switch (h.S)
        {
            case Stance.Down or Stance.Rise or Stance.Out or Stance.Corner or Stance.Cheer:
                return;
            case Stance.Cover:
                Blocked(w);
                return;
            case Stance.Guard when !ko && h.GuardHigh == high:
                Blocked(w);
                return;
            case Stance.Tell when h.Move is { } m:
                breaker = m.Breaker && !high && !ko && h.T >= h.Dur * BreakFrom && h.Blocked == 0;
                if (!m.Interrupt && !breaker && !ko)
                {
                    Blocked(w);
                    return;
                }
                counter = true;
                break;
            case Stance.Open or Stance.Taunt:
                counter = true;
                break;
        }
        double dmg = (ko ? KoDamage : high ? FaceDamage : BodyDamage) * (counter ? CounterFactor : 1);
        h.Hp -= dmg;
        w.Landed++;
        w.Points[0] += counter ? 2 : 1;
        int pts = ko ? KoPunchPoints : high ? FacePoints : BodyPoints;
        w.Score += counter ? pts * 2 : pts;
        if (counter) w.Counters++;
        if (!ko)
        {
            int was = y.Power;
            y.Power = Math.Min(PowerMax, y.Power + (counter ? 2 : 1));
            if (was < PowerMax && y.Power == PowerMax) w.Heard.Add(Sfx.Star);
        }
        h.Flinch = y.Side;
        h.FlinchHigh = high || ko;
        h.Blocked = 0;
        w.Heard.Add(high || ko ? Sfx.HitFace : Sfx.HitBody);
        w.Sparks.Add(new Spark(0, false, high || ko, y.Side, false, ko || counter));
        if (ko) { Call(w, "KO PUNCH!"); w.Shake = 16; }
        else if (breaker) Call(w, "BREAKER!", 1.2);
        else if (counter) Call(w, "COUNTER!");
        if (breaker) h.Hp = Math.Min(h.Hp, 0);
        if (h.Hp <= 0)
        {
            h.Hp = 0;
            Knockdown(w, Who.Him);
            return;
        }
        // How he takes it.
        h.Combo++;
        if (ko)
        {
            h.Move = null;
            Set(h, Stance.Open, KoStun);
            h.Combo = 0;
        }
        else if (h.S == Stance.Strike) { }
        else if (h.S is Stance.Open or Stance.Taunt)
        {
            if (h.S == Stance.Taunt) Set(h, Stance.Open, 0.8);
            if (h.Combo >= h.P.Cover + 2) Set(h, Stance.Cover, CoverTime);
        }
        else
        {
            h.Move = null;
            if (h.Combo >= h.P.Cover) Set(h, Stance.Cover, CoverTime);
            else Set(h, Stance.Hurt, HurtTime);
        }
        if (h.S == Stance.Cover) h.Combo = 0;
    }

    /// <summary>Into his gloves: you're thrown back a moment, and too many in a row and he jabs back at once.</summary>
    private static void Blocked(World w)
    {
        var y = w.You;
        var h = w.Him;
        y.Dur += Recoil;
        h.Blocked++;
        w.Heard.Add(Sfx.Block);
        w.Sparks.Add(new Spark(0, false, y.High, y.Side, true, false));
        if (h.S == Stance.Guard && !h.Still && h.Blocked >= h.P.Patience)
        {
            var jab = h.P.Moves[0];
            Throw(w, jab with { Name = "COUNTER JAB", Tell = CounterJabTell, Interrupt = false });
        }
    }

    // ───────────────────────────── him ─────────────────────────────

    private static double ThinkTime(World w) => w.P.ThinkMin + w.Dice.Next() * (w.P.ThinkMax - w.P.ThinkMin);

    /// <summary>His mind: guard, shifting it now and then; when he's thought long enough, a punch from his pattern (or a taunt).</summary>
    private static void Him(World w)
    {
        var h = w.Him;
        switch (h.S)
        {
            case Stance.Guard:
                if (h.Still) return;
                if (w.Dice.Next() < h.P.Switch * Field.Step) h.GuardHigh = !h.GuardHigh;
                h.Think -= Field.Step;
                if (h.Think <= 0) Decide(w);
                break;
            case Stance.Tell when h.T >= h.Dur:
                Set(h, Stance.Strike, h.Move!.Swing);
                w.Heard.Add(Sfx.Whoosh);
                break;
            case Stance.Strike when h.T >= h.Dur:
                Impact(w);
                break;
            case Stance.Recover or Stance.Hurt or Stance.Cover or Stance.Taunt or Stance.Open or Stance.Rise when h.T >= h.Dur:
                var was = h.S;
                Set(h, Stance.Guard, 0);
                // Hit, he guards where it came from.
                if (was == Stance.Hurt) h.GuardHigh = h.FlinchHigh;
                h.Move = null;
                h.Combo = 0;
                h.Think = was switch
                {
                    Stance.Cover => 0.15,
                    // Hit, he comes straight back at you.
                    Stance.Hurt => Math.Min(h.Think, 0.2 + w.Dice.Next() * 0.3),
                    Stance.Rise => 1.0,
                    _ => ThinkTime(w),
                };
                break;
        }
    }

    private static void Decide(World w)
    {
        var h = w.Him;
        var p = h.P;
        if (w.Dice.Next() < p.TauntChance)
        {
            Set(h, Stance.Taunt, TauntTime);
            return;
        }
        int i = w.Dice.Next() < p.Shuffle ? p.Pattern[w.Dice.Next(p.Pattern.Length)] : p.Pattern[h.Next++ % p.Pattern.Length];
        Throw(w, p.Moves[i]);
    }

    /// <summary>He starts winding up a punch (the tests use it to throw one).</summary>
    internal static void Throw(World w, Move m)
    {
        var h = w.Him;
        h.Move = m;
        h.Hit = 0;
        h.Side = m.Side;
        h.Clean = true;
        h.Blocked = 0;
        h.Combo = 0;
        Set(h, Stance.Tell, h.TellTime(m, 0));
        if (m.Special) w.Heard.Add(Sfx.Tell);
    }

    /// <summary>
    /// His glove arriving: stopped if your block or dodge is one that stops it (see <see cref="Move.Stops"/>), and if
    /// you slipped it (or blocked what can only be blocked) he's open for a counter. Otherwise it lands: it hurts, rocks
    /// you, and empties your power meter.
    /// </summary>
    private static void Impact(World w)
    {
        var h = w.Him;
        var y = w.You;
        var m = h.Move!;
        bool block = y.Act == Act.Block;
        int dodge = y.Evading ? y.Side : 0;
        if (Move.Stops(m.Kind, h.Side, block, dodge))
        {
            if (dodge != 0) w.Heard.Add(Sfx.Whiff);
            else
            {
                w.Heard.Add(Sfx.Block);
                w.Sparks.Add(new Spark(0, true, !m.Low, h.Side, true, false));
                if (m.Kind != Kind.Smash) h.Clean = false;
            }
        }
        else
        {
            h.Clean = false;
            y.Hp -= m.Damage * h.P.Power;
            Begin(y, Act.Hurt, HurtDur);
            y.Power = 0;
            w.Points[1]++;
            w.Heard.Add(m.Low ? Sfx.HitBody : Sfx.HitFace);
            w.Sparks.Add(new Spark(0, true, !m.Low, h.Side, false, m.Special));
            w.Shake = m.Special ? 14 : 7;
            if (y.Hp <= 0)
            {
                y.Hp = 0;
                Knockdown(w, Who.You);
                return;
            }
        }
        if (h.Hit + 1 < m.Hits)
        {
            h.Hit++;
            if (m.Alternate) h.Side = -h.Side;
            Set(h, Stance.Tell, h.TellTime(m, h.Hit));
            return;
        }
        if (h.Clean)
        {
            Set(h, Stance.Open, m.Opening);
            h.Combo = 0;
        }
        else Set(h, Stance.Recover, RecoverTime);
    }

    // ───────────────────────────── knockdowns, the count, the bell ─────────────────────────────

    private static void Knockdown(World w, Who who)
    {
        var y = w.You;
        var h = w.Him;
        int i = who == Who.You ? 0 : 1;
        w.Downed = who;
        w.Count = 0;
        w.CountT = 0;
        w.RoundDowns[i]++;
        w.Downs[i]++;
        w.Points[1 - i] += 5;
        w.Buffer = default;
        w.BufferT = 0;
        h.Move = null;
        w.Heard.Add(Sfx.Fall);
        w.Heard.Add(Sfx.Cheer);
        w.Heard.Add(Sfx.SayDown);
        Call(w, "DOWN!", 1.4);
        if (who == Who.You)
        {
            Begin(y, Act.Down, 0);
            y.Mash = 0;
            Set(h, Stance.Corner, 0);
        }
        else
        {
            Set(h, Stance.Down, 0);
            Begin(y, Act.Idle, 0);
            w.Score += KnockdownPoints;
        }
        if (w.RoundDowns[i] >= TkoDowns)
        {
            Finish(w, i == 0 ? Outcome.LostTko : Outcome.WonTko);
            Call(w, "T.K.O.", 99);
        }
    }

    /// <summary>The referee counts the downed boxer: him to the count he gets up at; you, once you've pressed enough.</summary>
    private static void Count(World w, Controls c)
    {
        var y = w.You;
        var h = w.Him;
        w.CountT += Field.Step;
        if (w.Downed == Who.You)
        {
            if (c.Pressed) y.Mash++;
            if (y.Mash >= RiseNeed(w.Downs[0]) && w.Count >= 2)
            {
                Begin(y, Act.Idle, 0);
                y.Hp = y.MaxHp * RiseShare(w.Downs[0]);
                w.Downed = Who.None;
                Set(h, Stance.Guard, 0);
                h.Think = 1.2;
                w.Heard.Add(Sfx.Cheer);
                return;
            }
        }
        if (w.CountT < CountSecond * (w.Count + 1)) return;
        w.Count++;
        w.Heard.Add(Sfx.SayOne + (w.Count - 1));
        if (w.Downed == Who.Him && w.Count < 10 && w.Count >= RiseAt(w))
        {
            Set(h, Stance.Rise, RiseTime);
            h.Hp = h.MaxHp * HisRiseShare(w.Downs[1]);
            w.Downed = Who.None;
            return;
        }
        if (w.Count >= 10)
        {
            w.Heard.Add(Sfx.SayKnockOut);
            Finish(w, w.Downed == Who.You ? Outcome.LostKo : Outcome.WonKo);
            Call(w, "KNOCK OUT!", 99);
        }
    }

    private static void EndRound(World w)
    {
        w.RoundOver = true;
        w.Heard.Add(Sfx.Bell);
        Call(w, "TIME!", 2);
        if (w.Round < Rounds) return;
        // The judges: whoever landed more, counting a knockdown as five.
        var o = w.Points[0] > w.Points[1] ? Outcome.WonDecision : w.Points[0] < w.Points[1] ? Outcome.LostDecision : Outcome.Draw;
        Finish(w, o);
    }

    /// <summary>The fight over: the winner's arms up.</summary>
    private static void Finish(World w, Outcome o)
    {
        w.Outcome = o;
        var y = w.You;
        var h = w.Him;
        h.Move = null;
        if (o.Won())
        {
            Begin(y, Act.Win, 0);
            if (o != Outcome.WonDecision) Set(h, Stance.Out, 0);
            else Set(h, Stance.Guard, 0);
        }
        else
        {
            if (y.Act == Act.Down || o != Outcome.LostDecision && o != Outcome.Draw) Begin(y, Act.Lose, 0);
            else Begin(y, Act.Idle, 0);
            Set(h, Stance.Cheer, 0);
        }
    }

    /// <summary>Between rounds: the next one, both boxers a little rested.</summary>
    public static void NextRound(World w)
    {
        var y = w.You;
        var h = w.Him;
        w.Round++;
        w.Clock = 0;
        w.RoundOver = false;
        w.RoundDowns = new int[2];
        w.Sparks.Clear();
        w.Buffer = default;
        w.BufferT = 0;
        w.Call = "";
        w.CallT = 0;
        Begin(y, Act.Idle, 0);
        y.Hp = Math.Min(y.MaxHp, y.Hp + 0.3 * y.MaxHp);
        h.Move = null;
        Set(h, Stance.Guard, 0);
        h.Hp = Math.Min(h.MaxHp, h.Hp + 0.15 * h.MaxHp);
        h.Think = 1.2;
        h.Combo = h.Blocked = 0;
    }
}
