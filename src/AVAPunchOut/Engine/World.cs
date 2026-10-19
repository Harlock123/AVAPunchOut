namespace AVAPunchOut.Engine;

/// <summary>The screen.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>
/// The controls for one 60th of a second. <see cref="Dy"/> is held: up (-1) makes a punch a high one, to the face,
/// and down (+1) blocks. The rest are presses, each taken once: a dodge left or right (<see cref="Dx"/>), a left or a
/// right punch, and the KO punch.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Left = false, bool Right = false, bool Ko = false)
{
    public bool Pressed => Dx != 0 || Left || Right || Ko;
    public Controls Presses => new(Dx, 0, Left, Right, Ko);
}

/// <summary>What you're doing.</summary>
public enum Act { Idle, Punch, KoPunch, Dodge, Block, Hurt, Down, Win, Lose }

/// <summary>What he's doing.</summary>
public enum Stance
{
    /// <summary>Guarding high or low, deciding what to throw.</summary>
    Guard,
    /// <summary>Winding up a punch: the moment to read it.</summary>
    Tell,
    /// <summary>The glove in the air.</summary>
    Strike,
    /// <summary>Getting his guard back after a punch that landed or was blocked.</summary>
    Recover,
    /// <summary>Off balance after a punch you slipped: hit him now.</summary>
    Open,
    Hurt,
    /// <summary>Both gloves up after too many in a row.</summary>
    Cover,
    /// <summary>Showing off: open to punches.</summary>
    Taunt,
    Down,
    Rise,
    /// <summary>In a neutral corner while you're counted.</summary>
    Corner,
    /// <summary>Counted out.</summary>
    Out,
    /// <summary>Arms up: he won.</summary>
    Cheer,
}

public enum Who { None, You, Him }

public enum Outcome { None, WonKo, WonTko, WonDecision, Draw, LostDecision, LostTko, LostKo }

public static class OutcomeExtensions
{
    public static bool Won(this Outcome o) => o is Outcome.WonKo or Outcome.WonTko or Outcome.WonDecision;
}

/// <summary>You: from behind, your stamina, and the power meter for the KO punch.</summary>
public sealed class Boxer
{
    public Act Act = Act.Idle;
    public double T, Dur;
    /// <summary>The glove of a punch (-1 left, +1 right), or the way of a dodge.</summary>
    public int Side = 1;
    public bool High, Struck;
    public double Hp = Sim.Stamina, MaxHp = Sim.Stamina;
    public int Power;
    /// <summary>Buttons pressed while you're down: enough and you get up.</summary>
    public int Mash;
    public Boxer Copy() => (Boxer)MemberwiseClone();
    public bool Ready => Act is Act.Idle or Act.Block || Act is Act.Punch or Act.KoPunch or Act.Dodge or Act.Hurt && T >= Dur;
    /// <summary>Out of the way of a punch: the body of a dodge.</summary>
    public bool Evading => Act == Act.Dodge && T >= Sim.DodgeFrom && T <= Sim.DodgeTo;
}

/// <summary>Him: big, facing you, guarding high or low until he throws.</summary>
public sealed class Opponent
{
    public Profile P = Circuit.Boxers[0];
    public Stance S = Stance.Guard;
    public double T, Dur;
    public Move? Move;
    /// <summary>Which hit of the move (from 0), the glove it's coming from, and whether every hit so far was stopped.</summary>
    public int Hit, Side;
    public bool Clean;
    public bool GuardHigh = true;
    public double Hp, MaxHp;
    /// <summary>Until he next does something of his own.</summary>
    public double Think;
    /// <summary>Punches taken in a row; your punches blocked in a row.</summary>
    public int Combo, Blocked;
    /// <summary>Where he is in his pattern.</summary>
    public int Next;
    /// <summary>The last punch he took: from which glove, and high or low (for the drawing).</summary>
    public int Flinch;
    public bool FlinchHigh;
    /// <summary>For the tests: he guards where he's put and throws nothing of his own.</summary>
    public bool Still;
    public Opponent Copy() => (Opponent)MemberwiseClone();
    /// <summary>How long a wind-up of his lasts.</summary>
    public double TellTime(Move m, int hit) => (hit == 0 ? m.Tell : m.Gap) * P.Pace;
}

/// <summary>A punch landing (or blocked), for the drawing: a burst at the glove that fades.</summary>
public readonly record struct Spark(double Age, bool OnYou, bool High, int Side, bool Blocked, bool Big);

/// <summary>
/// Everything in the ring that moves or changes, kept together so it can be copied whole: you, him, the round, the
/// clock and the count, the judges' points, the score.
/// </summary>
public sealed class World
{
    public Boxer You = new();
    public Opponent Him = new();
    public int Bout, Lap = 1;
    public int Round = 1;
    /// <summary>Game seconds into the round (it runs faster than real time).</summary>
    public double Clock;
    public bool RoundOver;
    public Outcome Outcome;
    /// <summary>Who's down, the count, and the time since he went down.</summary>
    public Who Downed;
    public int Count;
    public double CountT;
    /// <summary>Knockdowns this round and this fight: [0] you, [1] him.</summary>
    public int[] RoundDowns = new int[2], Downs = new int[2];
    /// <summary>The judges' points: [0] yours, [1] his.</summary>
    public int[] Points = new int[2];
    public int Landed, Thrown, Counters;
    public int Score;
    /// <summary>A press that came while you were busy, kept a moment for when you're ready.</summary>
    public Controls Buffer;
    public double BufferT;
    /// <summary>A word called out (COUNTER!, DOWN!) and how long it stays.</summary>
    public string Call = "";
    public double CallT;
    public double Shake;
    public Dice Dice = new(1);
    public List<Spark> Sparks = [];
    public List<Sfx> Heard = [];

    public Profile P => Him.P;

    public World Copy() => new()
    {
        You = You.Copy(), Him = Him.Copy(), Bout = Bout, Lap = Lap, Round = Round, Clock = Clock, RoundOver = RoundOver, Outcome = Outcome,
        Downed = Downed, Count = Count, CountT = CountT, RoundDowns = (int[])RoundDowns.Clone(), Downs = (int[])Downs.Clone(),
        Points = (int[])Points.Clone(), Landed = Landed, Thrown = Thrown, Counters = Counters, Score = Score, Buffer = Buffer, BufferT = BufferT,
        Call = Call, CallT = CallT, Shake = Shake, Dice = Dice, Sparks = [.. Sparks],
    };
}
