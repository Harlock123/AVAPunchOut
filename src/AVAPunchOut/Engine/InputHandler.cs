using System.Diagnostics;
using Avalonia.Input;

namespace AVAPunchOut.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: up held for a high punch, down held to block, and presses for the
/// rest (a dodge, a left or right punch, the KO punch). A press is a key going down that wasn't down already. The
/// keyboard's auto-repeat sends more key-downs while a key is held (and some systems send each repeat as a key-up then a
/// key-down at the same instant): neither is a press.
/// </summary>
public sealed class InputHandler : IDisposable
{
    /// <summary>A key down this soon after the same key came up is the keyboard repeating, not a finger.</summary>
    public const double RepeatGuard = 0.012;

    private static readonly Key[] DodgeLeft = [Key.Left, Key.A, Key.NumPad4];
    private static readonly Key[] DodgeRight = [Key.Right, Key.D, Key.NumPad6];
    private static readonly Key[] HighKeys = [Key.Up, Key.W, Key.NumPad8];
    private static readonly Key[] BlockKeys = [Key.Down, Key.S, Key.NumPad2];
    private static readonly Key[] LeftKeys = [Key.Z, Key.J];
    private static readonly Key[] RightKeys = [Key.X, Key.K];
    private static readonly Key[] KoKeys = [Key.Space, Key.Enter, Key.C, Key.L];

    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly Dictionary<Key, double> _upAt = [];
    private readonly ControllerManager _pad = new(0);
    private readonly Func<double> _now;
    private int _dx;
    private bool _left, _right, _ko;
    private bool _padLeft, _padRight, _padA, _padB, _padKo;

    /// <summary>The clock (seconds) is for the tests; it's the real one otherwise.</summary>
    public InputHandler(Func<double>? clock = null)
    {
        var watch = Stopwatch.StartNew();
        _now = clock ?? (() => watch.Elapsed.TotalSeconds);
    }

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        bool fresh = !_held.Contains(key) && !(_upAt.TryGetValue(key, out double up) && _now() - up < RepeatGuard);
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
        if (!fresh) return;
        if (DodgeLeft.Contains(key)) _dx = -1;
        else if (DodgeRight.Contains(key)) _dx = 1;
        else if (LeftKeys.Contains(key)) _left = true;
        else if (RightKeys.Contains(key)) _right = true;
        else if (KoKeys.Contains(key)) _ko = true;
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        double now = _now();
        if (_held.Remove(key)) _upAt[key] = now;
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was) && _held.Remove(was)) _upAt[was] = now;
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    /// <summary>Reads the gamepad: a button or the stick going over is a press, like a key.</summary>
    public void Update()
    {
        _pad.Update();
        bool left = PadLeft, right = PadRight, ko = PadKo;
        if (left && !_padLeft) _dx = -1;
        if (right && !_padRight) _dx = 1;
        if (_pad.ButtonA && !_padA) _left = true;
        if (_pad.ButtonB && !_padB) _right = true;
        if (ko && !_padKo) _ko = true;
        _padLeft = left;
        _padRight = right;
        _padA = _pad.ButtonA;
        _padB = _pad.ButtonB;
        _padKo = ko;
    }

    private bool PadKo => _pad.ButtonY || _pad.ButtonX || _pad.LeftBumper || _pad.RightBumper || _pad.RightTrigger > 0.5f;

    /// <summary>
    /// The controls since the last read. Up (W) held makes a punch high, down (S) held blocks; the presses: left and right
    /// (A, D) to dodge, Z (or J) a left punch, X (or K) a right punch, Space (Enter, C or L) the KO punch. On a gamepad:
    /// the stick or D-pad, A and B to punch, X, Y, a bumper or the right trigger for the KO punch. The presses are taken
    /// by the read.
    /// </summary>
    public Controls Read()
    {
        var pad = _pad;
        int dy = (Any(BlockKeys) || pad.DPadDown || pad.LeftStickY > 0.5f ? 1 : 0) - (Any(HighKeys) || pad.DPadUp || pad.LeftStickY < -0.5f ? 1 : 0);
        var c = new Controls(_dx, dy, _left, _right, _ko);
        _dx = 0;
        _left = _right = _ko = false;
        return c;
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
