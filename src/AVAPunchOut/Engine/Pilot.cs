namespace AVAPunchOut.Engine;

/// <summary>
/// The computer boxer, for the demonstration and the tests. It boxes the way the game is meant to be boxed: it waits,
/// watches his wind-up, and when it has read the punch it slips it the right way (or blocks what can only be blocked),
/// then counter-punches to the face while he's open, and throws the KO punch as soon as the meter is full. It beats him
/// to an interruptible punch now and then, knows a breaker when it sees one, and probes his guard where it isn't. It
/// needs a fifth of a second to see a wind-up, and the shorter the wind-up the likelier it misreads one: so it wins the
/// early fights easily, and the circuit gets the better of it in the end.
/// </summary>
public sealed class Pilot
{
    /// <summary>How long it takes to see a wind-up.</summary>
    public const double Reaction = 0.2;

    private Dice _dice = new(77);
    private Move? _move;
    private int _hit = -1, _dodge, _mash;
    private bool _misread, _left;
    private double _seen;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _move = null;
        _hit = -1;
        _mash = 0;
        Doing = "";
    }

    /// <summary>The chance of misreading a punch that can be seen for <paramref name="visible"/> seconds before it lands.</summary>
    public static double MissChance(double visible) => Math.Clamp(1.05 - 1.2 * visible, 0.02, 0.6);

    public Controls Decide(GameEngine g) => g.Phase != Phase.Fight ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        var y = w.You;
        var h = w.Him;
        if (w.Outcome != Outcome.None || w.RoundOver) return default;
        if (w.Downed == Who.You)
        {
            // Up off the canvas: pressing as fast as it can.
            Doing = "getting up";
            _mash++;
            return _mash % 4 == 0 ? new Controls(Left: _mash % 8 == 0, Right: _mash % 8 != 0) : default;
        }
        if (w.Downed == Who.Him) { Doing = "in a neutral corner"; return default; }
        if (h.S is Stance.Tell or Stance.Strike && h.Move is { } m) return Defend(w, m);
        _move = null;
        if (!y.Ready) return default;
        bool full = y.Power >= Sim.PowerMax;
        switch (h.S)
        {
            case Stance.Open or Stance.Taunt or Stance.Hurt:
                Doing = "counter-punching";
                return full ? new Controls(Ko: true) : Punch(high: true);
            case Stance.Guard:
                Doing = "watching";
                if (full) return new Controls(Ko: true);
                // Now and then a punch where his guard isn't, but never into it twice.
                if (h.Blocked == 0 && _dice.Next() < 0.025) { Doing = "probing"; return Punch(!h.GuardHigh); }
                return default;
        }
        return default;
    }

    private Controls Punch(bool high)
    {
        _left = !_left;
        return new Controls(Dy: high ? -1 : 0, Left: _left, Right: !_left);
    }

    private Controls Defend(World w, Move m)
    {
        var y = w.You;
        var h = w.Him;
        if (m != _move || h.Hit != _hit)
        {
            _move = m;
            _hit = h.Hit;
            _seen = 0;
            // The later hits of a combination are expected: they count as seen a moment sooner.
            double visible = (h.S == Stance.Tell ? h.Dur : 0) + m.Swing + (h.Hit > 0 ? 0.25 : 0);
            _misread = _dice.Next() < MissChance(visible);
            _dodge = h.Side != 0 ? -h.Side : _dice.Next(2) * 2 - 1;
        }
        _seen += Field.Step;
        if (_seen < Reaction || _misread) return y.Act == Act.Block ? new Controls(Dy: 1) : default;
        Doing = $"reading the {m.Name.ToLowerInvariant()}";
        double toImpact = h.S == Stance.Tell ? h.Dur - h.T + m.Swing : h.Dur - h.T;
        if (h.S == Stance.Tell && y.Ready)
        {
            // A breaker: to the body, late in the wind-up.
            if (m.Breaker && h.T >= h.Dur * (Sim.BreakFrom + 0.05) && h.Blocked == 0) { Doing = "breaking it"; return Punch(high: false); }
            // Beat him to it, if there's time.
            if (m.Interrupt && toImpact > 0.3 && _dice.Next() < 0.012) { Doing = "beating him to it"; return Punch(high: true); }
        }
        if (Move.Stops(m.Kind, h.Side, false, _dodge))
        {
            if (toImpact <= Sim.DodgeTo - 0.1 && y.Ready) return new Controls(Dx: _dodge);
            return default;
        }
        return Move.Stops(m.Kind, h.Side, true, 0) ? new Controls(Dy: 1) : default;
    }
}
