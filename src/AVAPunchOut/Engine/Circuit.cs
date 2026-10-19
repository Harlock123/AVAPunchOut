namespace AVAPunchOut.Engine;

/// <summary>
/// What a punch of his is, and so what stops it: a jab or a body blow can be blocked or slipped either way; a hook
/// blocked, or slipped away from the glove (slip into it and it lands); an uppercut can't be blocked, only slipped; a
/// haymaker only slipped away from; and a smash, too wide to slip, only blocked.
/// </summary>
public enum Kind { Jab, Hook, Body, Upper, Haymaker, Smash }

/// <summary>His wind-up: how each opponent gives his punches away (and how the screen shows it).</summary>
public enum Tell { Blush, Glint, Raise, Wink, Wobble, Dim }

public enum Hair { Parted, Bandana, Braids, Pompadour, Shaved, FlatTop }

/// <summary>
/// One of his punches. <paramref name="Side"/> is the glove it comes with, as you see it: -1 the one on the left of
/// the screen (his right hand), +1 the right, 0 straight down the middle. The tell is how long he winds up (the
/// opening for you to read it), the swing how long the glove is then in the air. Dodge or block it and he's open for
/// the <paramref name="Opening"/>. A punch with more than one hit comes again after the gap, from the other glove if
/// it alternates. Land a punch in his wind-up and, if it can be interrupted, you beat him to it; a breaker is a wind-up
/// a body punch knocks him flat out of.
/// </summary>
public sealed record Move(
    string Name, Kind Kind, int Side, double Tell, double Damage, double Opening,
    double Swing = 0.13, int Hits = 1, double Gap = 0.3, bool Interrupt = false, bool Breaker = false, bool Special = false, bool Alternate = false)
{
    public bool Low => Kind is Kind.Body or Kind.Smash;

    /// <summary>Whether a block stops it, and a dodge to the left or right slips it, coming from <paramref name="side"/>.</summary>
    public static bool Stops(Kind kind, int side, bool block, int dodge) => kind switch
    {
        Kind.Jab or Kind.Body => block || dodge != 0,
        Kind.Hook => block || dodge != 0 && dodge == -side,
        Kind.Upper => dodge != 0,
        Kind.Haymaker => dodge != 0 && dodge == -side,
        Kind.Smash => block,
        _ => false,
    };
}

/// <summary>How he looks: drawn from these, nothing else.</summary>
public sealed record Look(
    uint Skin, uint HairColour, uint Trunks, uint Trim, uint Gloves, Hair Hair, Tell Tell,
    double Width = 1, double Belly = 0, bool Moustache = false, bool Beard = false, uint Sash = 0);

/// <summary>
/// An opponent on the circuit: his card (record, weight, home town, his taunt), his strength and speed, how well he
/// guards and reads you, his punches and the order he throws them, and the count he gets up at after each knockdown
/// (10 or more: he stays down).
/// </summary>
public sealed record Profile(
    string Name, string Nick, string From, string Record, int Weight, string Taunt, Look Look,
    double Hp, double Skill, double Switch, int Patience, int Cover, double TauntChance, double ThinkMin, double ThinkMax,
    Move[] Moves, int[] Pattern, double Shuffle, int[] Rises, string TauntPose)
{
    /// <summary>Wind-ups are this much of the listed time (shorter the second time round the circuit).</summary>
    public double Pace { get; init; } = 1;
    /// <summary>His punches hurt this much more than listed.</summary>
    public double Power { get; init; } = 1;

    /// <summary>The same man the <paramref name="lap"/>th time round the circuit: quicker, harder, tougher, wiser, and up sooner.</summary>
    public Profile ForLap(int lap)
    {
        int k = Math.Max(0, lap - 1);
        if (k == 0) return this;
        return this with
        {
            Pace = Math.Pow(0.8, k),
            Power = 1 + 0.25 * k,
            Hp = Hp * (1 + 0.15 * k),
            Skill = Math.Min(0.85, Skill + 0.12 * k),
            ThinkMin = ThinkMin * Math.Pow(0.85, k),
            ThinkMax = ThinkMax * Math.Pow(0.85, k),
            Patience = Math.Max(1, Patience - k),
            Rises = Rises.Select(r => r >= 10 ? r : Math.Max(2, r - 2 * k)).ToArray(),
        };
    }
}

/// <summary>The circuit: six opponents, each harder than the last, then round again, harder still.</summary>
public static class Circuit
{
    // The early opponents' ordinary punches can be beaten to with a punch in the wind-up; the later ones' can't.
    private static Move Jab(double tell, double dmg, bool slow = false) => new("JAB", Kind.Jab, 0, tell, dmg, 0.5, Interrupt: slow);
    private static Move Hook(int side, double tell, double dmg, bool slow = false) =>
        new(side < 0 ? "RIGHT HOOK" : "LEFT HOOK", Kind.Hook, side, tell, dmg, 0.6, Swing: 0.15, Interrupt: slow);
    private static Move Body(int side, double tell, double dmg, bool slow = false) => new("BODY BLOW", Kind.Body, side, tell, dmg, 0.5, Interrupt: slow);

    public static readonly Profile[] Boxers =
    [
        new("TEACUP TERRY", "THE GENTLEMAN", "LITTLE PUDDLEBY, ENGLAND", "2-31 (1 KO)", 118,
            "OH DEAR. DO MIND THE FACE, WOULD YOU?",
            new(0xFFF4D2BC, 0xFFD8763A, 0xFFF0A0C0, 0xFFFFFFFF, 0xFFE87AA8, Hair.Parted, Tell.Blush, Width: 0.86, Moustache: true),
            Hp: 60, Skill: 0.12, Switch: 0.15, Patience: 4, Cover: 4, TauntChance: 0.22, ThinkMin: 1.2, ThinkMax: 2.4,
            Moves: [Jab(1.0, 7, true), Hook(-1, 1.1, 9, true), Hook(1, 1.1, 9, true), Body(0, 1.0, 7, true),
                new("TEA TIME", Kind.Haymaker, -1, 1.6, 14, 1.5, Swing: 0.2, Special: true)],
            Pattern: [0, 1, 0, 2, 3, 0, 4], Shuffle: 0.1, Rises: [7, 10], TauntPose: "A SIP OF TEA"),
        new("ROCKET REYES", "THE BLUR", "MONTERREY, MEXICO", "14-6 (9 KO)", 142,
            "BLINK AND YOU'LL MISS ME, AMIGO!",
            new(0xFFC88A5C, 0xFF1A1418, 0xFF2EA860, 0xFFF0C838, 0xFFE03A2E, Hair.Bandana, Tell.Glint, Width: 0.92, Sash: 0xFFE03A2E),
            Hp: 120, Skill: 0.4, Switch: 0.3, Patience: 3, Cover: 4, TauntChance: 0.12, ThinkMin: 0.4, ThinkMax: 1.0,
            Moves: [Jab(0.8, 8, true), Hook(-1, 0.85, 10, true), Hook(1, 0.85, 10, true), Body(-1, 0.8, 9), Body(1, 0.8, 9),
                new("ROCKET FLURRY", Kind.Upper, 0, 1.2, 9, 1.3, Swing: 0.12, Hits: 3, Gap: 0.3, Special: true)],
            Pattern: [0, 0, 1, 3, 2, 4, 0, 5], Shuffle: 0.15, Rises: [6, 9], TauntPose: "A LITTLE DANCE"),
        new("GRANITE GUNNAR", "THE AVALANCHE", "TRONDHEIM, NORWAY", "21-3 (18 KO)", 236,
            "THE MOUNTAIN DOES NOT MOVE. YOU WILL.",
            new(0xFFF0D8C8, 0xFFF0D070, 0xFF203C8C, 0xFFF0F0F0, 0xFF3A3A44, Hair.Braids, Tell.Raise, Width: 1.22, Beard: true),
            Hp: 170, Skill: 0.7, Switch: 0.2, Patience: 3, Cover: 3, TauntChance: 0.14, ThinkMin: 0.7, ThinkMax: 1.5,
            Moves: [Jab(0.9, 11), Hook(-1, 0.95, 14), Hook(1, 0.95, 14), Body(0, 0.9, 12),
                new("AVALANCHE", Kind.Upper, 0, 1.5, 28, 1.5, Swing: 0.18, Breaker: true, Special: true)],
            Pattern: [0, 1, 3, 2, 0, 4], Shuffle: 0.15, Rises: [4, 7, 10], TauntPose: "A FLEX"),
        new("FLASH FONTAINE", "THE SHOWMAN", "MARSEILLE, FRANCE", "26-2 (20 KO)", 151,
            "THE CROWD CAME TO SEE ME, DARLING. TRY TO KEEP UP.",
            new(0xFFD8A878, 0xFF141018, 0xFF7A3AB8, 0xFFE0E0F0, 0xFFF4F4FA, Hair.Pompadour, Tell.Wink, Width: 0.98, Moustache: true),
            Hp: 180, Skill: 0.6, Switch: 0.6, Patience: 2, Cover: 3, TauntChance: 0.15, ThinkMin: 0.4, ThinkMax: 1.0,
            Moves: [Jab(0.6, 9),
                new("TWIN HOOKS", Kind.Hook, -1, 0.8, 10, 0.9, Swing: 0.14, Hits: 2, Gap: 0.32, Alternate: true),
                new("TWIN HOOKS", Kind.Hook, 1, 0.8, 10, 0.9, Swing: 0.14, Hits: 2, Gap: 0.32, Alternate: true),
                Body(-1, 0.65, 10),
                new("MIRRORBALL", Kind.Haymaker, -1, 1.0, 22, 1.3, Swing: 0.16, Special: true),
                new("MIRRORBALL", Kind.Haymaker, 1, 1.0, 22, 1.3, Swing: 0.16, Special: true)],
            Pattern: [0, 1, 0, 3, 2, 4, 0, 5], Shuffle: 0.35, Rises: [5, 8], TauntPose: "A BLOWN KISS"),
        new("BIG BISCUIT BARROW", "THE GRAVY TRAIN", "MEMPHIS, TENNESSEE", "30-5 (24 KO)", 268,
            "I HAD FOUR BREAKFASTS. YOU'RE NUMBER FIVE.",
            new(0xFF8A5A3A, 0xFF1A1210, 0xFFF08A28, 0xFFFFF0D0, 0xFF6A3A22, Hair.Shaved, Tell.Wobble, Width: 1.3, Belly: 1, Beard: true),
            Hp: 210, Skill: 0.6, Switch: 0.4, Patience: 2, Cover: 3, TauntChance: 0.1, ThinkMin: 0.5, ThinkMax: 1.1,
            Moves: [Jab(0.75, 11), Hook(-1, 0.8, 14), Hook(1, 0.8, 14), Body(-1, 0.75, 13), Body(1, 0.75, 13),
                new("GRAVY TRAIN", Kind.Smash, 0, 1.3, 26, 1.3, Swing: 0.2, Special: true)],
            Pattern: [0, 3, 1, 5, 4, 2, 0, 5], Shuffle: 0.25, Rises: [3, 6, 9], TauntPose: "A BELLY PAT"),
        new("LIGHTS OUT LAZARUS", "THE CHAMPION", "DETROIT, MICHIGAN", "38-0 (35 KO)", 205,
            "LIGHTS OUT, KID. THEY ALWAYS GO OUT.",
            new(0xFF5A3826, 0xFF16121A, 0xFF16161C, 0xFFE8C040, 0xFFE8C040, Hair.FlatTop, Tell.Dim, Width: 1.08, Beard: true, Sash: 0xFFE8C040),
            Hp: 250, Skill: 0.75, Switch: 0.6, Patience: 2, Cover: 3, TauntChance: 0.08, ThinkMin: 0.3, ThinkMax: 0.9,
            Moves: [Jab(0.5, 12), Hook(-1, 0.6, 15), Hook(1, 0.6, 15), Body(0, 0.55, 13),
                new("LIGHTS OUT", Kind.Upper, 0, 0.95, 22, 1.4, Swing: 0.12, Hits: 3, Gap: 0.28, Special: true),
                new("MIDNIGHT", Kind.Haymaker, -1, 0.8, 26, 1.3, Swing: 0.15, Special: true),
                new("MIDNIGHT", Kind.Haymaker, 1, 0.8, 26, 1.3, Swing: 0.15, Special: true)],
            Pattern: [0, 1, 3, 2, 0, 4, 5, 0, 6, 4], Shuffle: 0.4, Rises: [3, 5, 8], TauntPose: "A BECKON"),
    ];

    public static int Count => Boxers.Length;

    /// <summary>The <paramref name="bout"/>th opponent (from 0) on the <paramref name="lap"/>th time round.</summary>
    public static Profile Get(int bout, int lap = 1) => Boxers[bout % Count].ForLap(lap);
}
