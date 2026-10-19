using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPunchOut.Engine;

namespace AVAPunchOut.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPunchOut/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30, Green = 0xFF5AF07A;

    /// <summary>Your boxer's name.</summary>
    public const string YourName = "KID BLAZE";

    private static FormattedText Formatted(string text, double size, uint colour) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = Formatted(text, size, colour);
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(Formatted(text, size, 0xC0000000), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    /// <summary>Big words with a thick outline, the way the arcade called them.</summary>
    private static void Shout(DrawingContext dc, string text, Point at, double size, uint fill, uint edge, double edgeWidth = 8)
    {
        var ft = Formatted(text, size, fill);
        var origin = new Point(at.X - ft.Width / 2, at.Y - ft.Height / 2);
        if (ft.BuildGeometry(origin) is { } g)
        {
            dc.DrawGeometry(null, Pen(0xA0000000, edgeWidth + 6), g);
            dc.DrawGeometry(Brush(fill), Pen(edge, edgeWidth), g);
            dc.DrawGeometry(Brush(fill), null, g);
        }
        else dc.DrawText(ft, origin);
    }

    /// <summary>Wraps <paramref name="text"/> into lines of at most <paramref name="width"/> characters.</summary>
    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        string line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { lines.Add(line); line = word; }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    /// <summary>A smooth shape through the points (a Catmull-Rom curve), closed or open.</summary>
    private static StreamGeometry Smooth(IReadOnlyList<Point> p, bool closed = true)
    {
        var g = new StreamGeometry();
        int n = p.Count;
        using (var c = g.Open())
        {
            c.BeginFigure(p[0], closed);
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var p0 = p[closed ? (i - 1 + n) % n : Math.Max(0, i - 1)];
                var p1 = p[i];
                var p2 = p[(i + 1) % n];
                var p3 = p[closed ? (i + 2) % n : Math.Min(n - 1, i + 2)];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                c.CubicBezierTo(c1, c2, p2);
            }
            c.EndFigure(closed);
        }
        return g;
    }

    private static void Blob(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts) =>
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, Smooth(pts));

    private static void Curve(DrawingContext dc, uint colour, double width, params Point[] pts) =>
        dc.DrawGeometry(null, Pen(colour, width), Smooth(pts, closed: false));

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static uint Blend(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Shade(uint c, double k) => Blend(c, k < 0 ? 0xFF000000 : 0xFFFFFFFF, Math.Abs(k));

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static string Clock(double seconds)
    {
        int s = (int)seconds;
        return $"{s / 60}:{s % 60:00}";
    }

    private bool Blink(double rate = 2) => (int)(_clock * rate) % 2 == 0;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawArena(dc, null, 0.5); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawArena(dc, null, 0.5); DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                if (g.Phase == Phase.Intro) DrawCard(dc);
                else DrawFight(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(262, 742, 500, 24), 6);
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 753), 22, Yellow, center: true);
        }
    }

    // ───────────────────────────── the arena ─────────────────────────────

    /// <summary>Where the ring's far edge is, and its corner posts.</summary>
    private const double FarY = 352, PostL = 128, PostR = 896;
    private static readonly double[] RopeY = [248, 284, 320];
    private static readonly uint[] RopeColour = [0xFFE03A3A, 0xFFF0F0F0, 0xFF3A5AE0];

    /// <summary>
    /// The arena: the dark hall, the crowd in rows with cameras flashing, the far ropes and posts, and the canvas. The
    /// lights go down by <paramref name="dark"/> (0 to 1) for the champion's tell, or behind a menu.
    /// </summary>
    private void DrawArena(DrawingContext dc, World? w, double dark = 0)
    {
        for (int i = 0; i < 12; i++)
            dc.FillRectangle(Brush(Blend(0xFF06060E, 0xFF1E1630, i / 11.0)), new Rect(0, i * 30, Field.Width, 31));
        // The lights above, and their beams.
        for (int i = 0; i < 5; i++)
        {
            double x = 112 + i * 200;
            double spread = (512 - x) * 0.35;
            Poly(dc, 0x0CFFF4D0, null, 0, new Point(x - 14, 0), new Point(x + 14, 0), new Point(x + spread + 110, FarY), new Point(x + spread - 110, FarY));
            dc.FillEllipse(x, 4, 10, 0xFFFFF4D0);
        }
        // The crowd, rows of them, nearer rows bigger and brighter.
        bool roar = w != null && (w.Downed != Who.None || w.Outcome != Outcome.None);
        for (int row = 0; row < 5; row++)
        {
            double y = 150 + row * 34, size = 0.62 + row * 0.1, gap = 34 + row * 5;
            uint tint = Blend(0xFF141020, 0xFF4A3E58, row / 4.0);
            for (int k = 0; k < Field.Width / gap + 2; k++)
            {
                uint hsh = Hash(k, row, 3);
                double x = k * gap + (row % 2) * gap / 2 - 10 + hsh % 9;
                double bob = roar && hsh % 3 == 0 ? Math.Abs(Math.Sin(_clock * 9 + k)) * 8 : 0;
                uint shirt = Blend(tint, new[] { 0xFF8A3030, 0xFF305A8A, 0xFF8A7A30, 0xFF3A7A4A, 0xFF6A6A7A }[hsh % 5], 0.35);
                double hy = y - bob;
                dc.DrawRectangle(Brush(shirt), null, new Rect(x - 15 * size, hy + 6 * size, 30 * size, 40 * size), 8 * size, 8 * size);
                dc.FillEllipse(x, hy, 10 * size, Blend(tint, new[] { 0xFFE0B090, 0xFF9A6A48, 0xFF5A3A28, 0xFFF0C8A8 }[(hsh >> 4) % 4], 0.45));
                if (roar && hsh % 4 == 1)
                {
                    dc.DrawLine(Pen(shirt, 6 * size), new Point(x - 12 * size, hy + 12 * size), new Point(x - 20 * size, hy - 22 * size - bob));
                    dc.DrawLine(Pen(shirt, 6 * size), new Point(x + 12 * size, hy + 12 * size), new Point(x + 20 * size, hy - 22 * size - bob));
                }
            }
        }
        // Cameras flashing in the crowd.
        int slot = (int)(_clock * 7);
        for (int i = 0; i < 3; i++)
        {
            uint hsh = Hash(slot, i, 11);
            if (hsh % 3 != 0) continue;
            double fx = hsh % 1000 / 1000.0 * Field.Width, fy = 140 + (hsh >> 10) % 150;
            Star(dc, fx, fy, 14, 4, 0xE0FFFFFF);
            dc.FillEllipse(fx, fy, 22, 0x30FFFFFF);
        }
        // The ring: the apron, the canvas, the far posts and ropes, the side ropes coming toward you.
        Poly(dc, 0xFF1A2A5A, null, 0, new Point(PostL - 30, FarY - 6), new Point(PostR + 30, FarY - 6), new Point(PostR + 30, FarY + 14), new Point(PostL - 30, FarY + 14));
        Poly(dc, 0xFFA8B4CC, null, 0, new Point(PostL, FarY), new Point(PostR, FarY), new Point(1160, Field.Height), new Point(-136, Field.Height));
        // Light pooled in the middle of the canvas, and the logo.
        for (int i = 0; i < 6; i++) dc.DrawEllipse(Brush(0x0EFFFFFF), null, new Point(512, 560), 470 - i * 70, 150 - i * 22);
        dc.DrawEllipse(null, Pen(0x40304A8A, 6), new Point(512, 560), 250, 70);
        Text(dc, "A V A", new Point(512, 560), 60, 0x40304A8A, center: true, shadow: false);
        for (int i = 0; i < 3; i++)
        {
            double ry = RopeY[i];
            // Side ropes: from the far posts out to beside you, spreading about eye level.
            double near = 300 + (ry - 300) * 4.2;
            dc.DrawLine(Pen(Shade(RopeColour[i], -0.25), 9), new Point(PostL, ry), new Point(-60, near));
            dc.DrawLine(Pen(Shade(RopeColour[i], -0.25), 9), new Point(PostR, ry), new Point(Field.Width + 60, near));
        }
        foreach (double px in new[] { PostL, PostR })
        {
            dc.FillRectangle(Brush(0xFF9AA0B0), new Rect(px - 9, 226, 18, FarY - 220));
            dc.FillRectangle(Brush(0xFF6A7080), new Rect(px + 3, 226, 6, FarY - 220));
            for (int i = 0; i < 3; i++)
                dc.DrawRectangle(Brush(px < 512 ? 0xFFC83030 : 0xFF3050C8), Pen(0xFF101018, 2), new Rect(px - 13, RopeY[i] - 9, 26, 18), 5, 5);
        }
        for (int i = 0; i < 3; i++)
        {
            double ry = RopeY[i];
            Curve(dc, Shade(RopeColour[i], -0.45), 7, new Point(PostL + 12, ry + 2), new Point(512, ry + 7), new Point(PostR - 12, ry + 2));
            Curve(dc, RopeColour[i], 4, new Point(PostL + 12, ry), new Point(512, ry + 5), new Point(PostR - 12, ry));
        }
        if (dark > 0) dc.FillRectangle(Brush(Alpha(0xFF000008, dark)), new Rect(0, 0, Field.Width, Field.Height));
    }

    private static void Star(DrawingContext dc, double x, double y, double r, int points, uint colour, double inner = 0.35, double turn = 0)
    {
        var pts = new Point[points * 2];
        for (int i = 0; i < points * 2; i++)
        {
            double a = turn + Math.PI * i / points - Math.PI / 2, rr = i % 2 == 0 ? r : r * inner;
            pts[i] = new Point(x + Math.Cos(a) * rr, y + Math.Sin(a) * rr);
        }
        Poly(dc, colour, null, 0, pts);
    }

    // ───────────────────────────── the fight ─────────────────────────────

    /// <summary>How dark the champion's tell makes the hall: the lights go down as he winds up.</summary>
    private static double Darkness(World w)
    {
        var h = w.Him;
        if (h.P.Look.Tell != Tell.Dim || h.Move is not { Special: true }) return 0;
        if (h.S == Stance.Tell) return 0.55 * Math.Min(1, h.T / Math.Max(0.05, h.Dur) * 1.6);
        return h.S == Stance.Strike ? 0.55 : 0;
    }

    private void DrawFight(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        double shake = w.Shake;
        double sx = shake > 0 ? Math.Sin(_clock * 93) * shake : 0, sy = shake > 0 ? Math.Cos(_clock * 71) * shake * 0.5 : 0;
        using (dc.PushTransform(Matrix.CreateTranslation(sx, sy)))
        {
            DrawArena(dc, w, Darkness(w));
            DrawRing(dc, w);
        }
        DrawHud(dc, w);
        DrawCalls(dc, w);
    }

    /// <summary>Inside the ropes: the referee, him, you, and the punches landing.</summary>
    private void DrawRing(DrawingContext dc, World w)
    {
        var h = w.Him;
        if (w.Downed == Who.You || h.S is Stance.Corner)
        {
            // You're down: he waits in a neutral corner, small and far off; the referee stands over you.
            DrawOpponentAt(dc, h, 230, 452, 0.56);
            DrawReferee(dc, w, 512, 600, 1.0, counting: true);
        }
        else if (h.S is Stance.Down or Stance.Out)
        {
            DrawReferee(dc, w, 790, 452, 0.78, counting: w.Downed == Who.Him);
            DrawOpponentDown(dc, h);
        }
        else
        {
            DrawReferee(dc, w, 860, 400, 0.6, counting: false);
            double sway = h.S == Stance.Guard ? Math.Sin(_clock * 2.1) * 10 : 0;
            DrawOpponentAt(dc, h, 512 + sway, 452, 1.0);
        }
        DrawYou(dc, w);
        foreach (var s in w.Sparks) DrawSpark(dc, w, s);
    }

    private void DrawSpark(DrawingContext dc, World w, Spark s)
    {
        double k = s.Age / 0.45;
        Point at = s.OnYou
            ? new Point(512 + s.Side * 40, s.High ? 520 : 650)
            : new Point(512 + s.Side * 34, s.High ? 205 : 370);
        if (s.Blocked)
        {
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3 + 0.3, r0 = 14 + k * 30, r1 = r0 + 16;
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 1 - k), 3), new Point(at.X + Math.Cos(a) * r0, at.Y + Math.Sin(a) * r0), new Point(at.X + Math.Cos(a) * r1, at.Y + Math.Sin(a) * r1));
            }
            return;
        }
        double r = (s.Big ? 90 : 58) * (0.5 + k);
        Star(dc, at.X, at.Y, r, 9, Alpha(s.OnYou ? 0xFFFF3A2A : 0xFFFFE14A, 1 - k), 0.45, k);
        Star(dc, at.X, at.Y, r * 0.55, 9, Alpha(0xFFFFFFFF, 1 - k), 0.5, -k);
        if (s.Big && !s.OnYou) Shout(dc, "POW!", new Point(at.X + s.Side * 120, at.Y - 20 - k * 30), 50, Alpha(0xFFFFFFFF, 1 - k), Alpha(0xFFE03020, 1 - k), 4);
    }

    /// <summary>The words called out: COUNTER!, DOWN!, the count, ROUND 1, FIGHT!, and the result.</summary>
    private void DrawCalls(DrawingContext dc, World w)
    {
        var g = Engine;
        switch (g.Phase)
        {
            case Phase.Bell:
                if (g.PhaseTime < GameEngine.BellSeconds * 0.5) Shout(dc, $"ROUND {w.Round}", new Point(512, 380), 110, Yellow, 0xFFC02020);
                else Shout(dc, "FIGHT!", new Point(512, 380), 130 + Math.Sin(g.PhaseTime * 20) * 4, White, 0xFFC02020);
                return;
            case Phase.Break:
                DrawBreak(dc, w);
                return;
            case Phase.Result:
                DrawResult(dc, w);
                return;
        }
        if (w.Downed != Who.None)
        {
            if (w.Count > 0)
            {
                double pop = Math.Max(0, 1 - (w.CountT - w.Count * Sim.CountSecond) * 4);
                Shout(dc, w.Count.ToString(), new Point(w.Downed == Who.You ? 760 : 560, 250), 150 + pop * 40, Yellow, 0xFF101010);
            }
            else if (w.CallT > 0) Shout(dc, w.Call, new Point(512, 260), 110, Red, 0xFF101010);
            if (w.Downed == Who.You)
            {
                // Get up: the presses needed, filling.
                int need = Sim.RiseNeed(w.Downs[0]);
                double f = Math.Min(1, (double)w.You.Mash / need);
                dc.FillRectangle(Brush(0xD0000000), new Rect(312, 616, 400, 80), 10);
                Text(dc, Blink(4) ? "PUNCH! PUNCH! GET UP!" : "", new Point(512, 640), 34, Yellow, center: true);
                dc.FillRectangle(Brush(0xFF303040), new Rect(332, 664, 360, 18), 4);
                dc.FillRectangle(Brush(Blend(Red, Green, f)), new Rect(332, 664, 360 * f, 18), 4);
            }
            return;
        }
        if (w.CallT > 0 && w.Call != "")
        {
            double k = 1 - Math.Min(1, w.CallT);
            // Called out to the side, over the crowd, clear of his head.
            Shout(dc, w.Call, new Point(212, 196 - k * 20), 58, w.Call == "COUNTER!" ? Yellow : Orange, 0xFF101010, 6);
        }
    }

    private void DrawBreak(DrawingContext dc, World w)
    {
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 96, Field.Width, Field.Height - 96));
        dc.FillRectangle(Brush(0xE0101020), new Rect(232, 170, 560, 360), 14);
        dc.DrawRectangle(null, Pen(Yellow, 3), new Rect(232, 170, 560, 360), 14, 14);
        Shout(dc, $"END OF ROUND {w.Round}", new Point(512, 222), 56, Yellow, 0xFF901010, 5);
        Text(dc, "THE JUDGES' CARDS", new Point(512, 280), 30, Dim, center: true);
        Text(dc, $"{YourName,-12}{w.Points[0],4}", new Point(512, 326), 38, Cyan, center: true);
        Text(dc, $"{Short(w.P.Name),-12}{w.Points[1],4}", new Point(512, 370), 38, Red, center: true);
        Text(dc, $"PUNCHES LANDED {w.Landed} OF {w.Thrown}   COUNTERS {w.Counters}", new Point(512, 428), 26, White, center: true);
        Text(dc, $"ROUND {w.Round + 1} OF {Sim.Rounds} NEXT", new Point(512, 484), 30, Blink() ? Yellow : White, center: true);
    }

    /// <summary>The short form of his name, for the bars and the cards: the last word, the way ring announcers do.</summary>
    private static string Short(string name) => name.Split(' ')[^1];

    private void DrawResult(DrawingContext dc, World w)
    {
        var g = Engine;
        var o = w.Outcome;
        string head = o switch
        {
            Outcome.WonKo or Outcome.LostKo => "KNOCK OUT!",
            Outcome.WonTko or Outcome.LostTko => "T.K.O.",
            Outcome.WonDecision => "DECISION",
            Outcome.Draw => "A DRAW",
            _ => "DECISION",
        };
        Shout(dc, head, new Point(512, 210), 120 + Math.Sin(_clock * 6) * 4, o.Won() ? Yellow : Red, 0xFF101010, 8);
        if (g.PhaseTime < 1.0) return;
        // The tally along the bottom, clear of the ring.
        dc.FillRectangle(Brush(0xD8000000), new Rect(212, o.Won() ? 586 : 640, 600, o.Won() ? 168 : 114), 14);
        if (o.Won())
        {
            Text(dc, $"{YourName} WINS!", new Point(512, 614), 40, Cyan, center: true);
            string how = o switch { Outcome.WonKo => "KNOCKOUT BONUS", Outcome.WonTko => "T.K.O. BONUS", _ => "DECISION BONUS" };
            Text(dc, $"{how,-16}{g.FinishBonus,8}", new Point(512, 656), 30, White, center: true);
            Text(dc, $"{"TIME BONUS",-16}{g.TimeBonus,8}", new Point(512, 690), 30, White, center: true);
            Text(dc, $"{"SCORE",-16}{g.Score,8}", new Point(512, 726), 30, Yellow, center: true);
        }
        else
        {
            Text(dc, o == Outcome.Draw ? "THE JUDGES CAN'T SPLIT YOU" : $"{w.P.Name} WINS", new Point(512, 670), 38, Red, center: true);
            Text(dc, g.State == GameState.Attract ? "" : g.Lives > 1 ? "A REMATCH AWAITS" : "YOUR CAREER IS OVER", new Point(512, 718), 30, White, center: true);
        }
    }

    // ───────────────────────────── the scoreboard ─────────────────────────────

    private void DrawHud(DrawingContext dc, World w)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xF0080810), new Rect(0, 0, Field.Width, 96));
        dc.FillRectangle(Brush(0xFF2A2A48), new Rect(0, 94, Field.Width, 3));
        // You on the left, him on the right.
        Text(dc, YourName, new Point(22, 20), 28, Cyan);
        Bar(dc, 22, 38, 360, w.You.Hp / w.You.MaxHp, false);
        Text(dc, $"SCORE {g.Score:000000}", new Point(22, 76), 26, White);
        Downs(dc, 382, 76, w.Downs[0], right: true);
        Text(dc, w.P.Name, new Point(1002, 20), 28, 0xFFFF7A6A, right: true);
        Bar(dc, 642, 38, 360, w.Him.Hp / w.Him.MaxHp, true);
        Text(dc, $"BOUT {w.Bout + 1}/{Circuit.Count}" + (w.Lap > 1 ? $"  CIRCUIT {w.Lap}" : ""), new Point(1002, 76), 24, Dim, right: true);
        Downs(dc, 642, 76, w.Downs[1], right: false);
        // The clock, the round, the power meter.
        Text(dc, Clock(w.Clock), new Point(512, 24), 46, Yellow, center: true);
        Text(dc, $"ROUND {w.Round}", new Point(512, 54), 22, White, center: true);
        var y = w.You;
        bool full = y.Power >= Sim.PowerMax;
        for (int i = 0; i < Sim.PowerMax; i++)
        {
            uint on = full ? (Blink(8) ? Yellow : Orange) : Blend(0xFF40A0FF, 0xFFFFE14A, i / (double)(Sim.PowerMax - 1));
            dc.FillRectangle(Brush(i < y.Power ? on : 0xFF262636), new Rect(432 + i * 16, 70, 13, 16), 2);
        }
        if (full) Shout(dc, "KO", new Point(612, 77), 30, Yellow, 0xFFC02020, 3);
        else Text(dc, "KO", new Point(612, 77), 24, 0xFF505068, center: true);
    }

    private static void Bar(DrawingContext dc, double x, double y, double width, double f, bool right)
    {
        f = Math.Clamp(f, 0, 1);
        dc.FillRectangle(Brush(0xFF262636), new Rect(x, y, width, 20), 4);
        uint c = f > 0.5 ? Blend(0xFFFFE14A, 0xFF5AF07A, (f - 0.5) * 2) : Blend(0xFFFF3A2A, 0xFFFFE14A, f * 2);
        double fw = width * f;
        dc.FillRectangle(Brush(c), new Rect(right ? x + width - fw : x, y, fw, 20), 4);
        dc.FillRectangle(Brush(0x50FFFFFF), new Rect(right ? x + width - fw : x, y + 2, fw, 5), 2);
        for (int i = 1; i < 10; i++) dc.FillRectangle(Brush(0x60000000), new Rect(x + i * width / 10, y, 2, 20));
    }

    private static void Downs(DrawingContext dc, double x, double y, int n, bool right)
    {
        for (int i = 0; i < Math.Min(n, 6); i++)
        {
            double cx = right ? x - 10 - i * 22 : x + 10 + i * 22;
            Star(dc, cx, y, 9, 5, Red, 0.45);
        }
    }

    // ───────────────────────────── the opponent's card ─────────────────────────────

    /// <summary>Between fights: the next opponent's card, the way the fight posters do it, and the circuit below.</summary>
    private void DrawCard(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var p = w.P;
        for (int i = 0; i < 16; i++)
            dc.FillRectangle(Brush(Blend(0xFF10183A, 0xFF2A0E2A, i / 15.0)), new Rect(0, i * 48, Field.Width, 49));
        for (int i = -8; i < 20; i++)
            Poly(dc, 0x0CFFFFFF, null, 0, new Point(i * 80, 0), new Point(i * 80 + 30, 0), new Point(i * 80 + 30 + 300, Field.Height), new Point(i * 80 + 300, Field.Height));
        // The portrait, under a spotlight.
        var frame = new Rect(36, 96, 440, 500);
        dc.FillRectangle(Brush(0xFF06060C), frame, 10);
        using (dc.PushClip(frame))
        {
            for (int i = 0; i < 8; i++) dc.DrawEllipse(Brush(0x12FFF0C0), null, new Point(256, 300), 260 - i * 26, 300 - i * 30);
            var pose = new Opponent { P = p, S = Stance.Guard, GuardHigh = true, T = _clock };
            DrawOpponentAt(dc, pose, 256, 560, 1.12, portrait: true);
        }
        dc.DrawRectangle(null, Pen(Yellow, 4), frame, 10, 10);
        Text(dc, g.Bout == Circuit.Count - 1 ? "THE CHAMPION" : "NEXT OPPONENT", new Point(746, 70), 32, Dim, center: true);
        Shout(dc, p.Name, new Point(746, 124), p.Name.Length > 16 ? 50 : 60, Yellow, 0xFF901010, 5);
        Text(dc, $"\"{p.Nick}\"", new Point(746, 170), 32, Cyan, center: true);
        double ly = 222;
        foreach (var (k, v) in new[] { ("FROM", p.From), ("RECORD", p.Record), ("WEIGHT", $"{p.Weight} LBS"), ("RANK", g.Bout == Circuit.Count - 1 ? "WORLD CHAMPION" : $"#{Circuit.Count - 1 - g.Bout} CONTENDER") })
        {
            Text(dc, k, new Point(512, ly), 28, Dim);
            Text(dc, v, new Point(640, ly), 28, White);
            ly += 36;
        }
        // His taunt, in a speech bubble pointing at him.
        var lines = Wrap(p.Taunt, 28);
        double bh = 30 + lines.Count * 32, by = 380;
        dc.FillRectangle(Brush(0xFFF6F2E8), new Rect(512, by, 470, bh), 16);
        Poly(dc, 0xFFF6F2E8, null, 0, new Point(520, by + 20), new Point(470, by + 10), new Point(520, by + 44));
        for (int i = 0; i < lines.Count; i++) Text(dc, lines[i], new Point(747, by + 30 + i * 32), 30, 0xFF201818, center: true, shadow: false);
        Text(dc, $"{YourName}   WON {g.Wins}  LOST {g.Losses}" + (g.Lap > 1 ? $"   CIRCUIT {g.Lap}" : ""), new Point(746, 560), 26, Cyan, center: true);
        // The circuit: each opponent in a little frame, those beaten crossed out.
        for (int i = 0; i < Circuit.Count; i++)
        {
            var box = new Rect(36 + i * 160, 616, 148, 118);
            bool now = i == g.Bout, beaten = i < g.Bout;
            dc.FillRectangle(Brush(now ? 0xFF2A2440 : 0xFF0C0C16), box, 8);
            using (dc.PushClip(box))
            {
                var pose = new Opponent { P = Circuit.Get(i, g.Lap), S = Stance.Guard, GuardHigh = false, T = now ? _clock : 0 };
                DrawOpponentAt(dc, pose, box.X + 74, box.Y + 196, 0.52, portrait: true);
                if (!now) dc.FillRectangle(Brush(beaten ? 0x80000000 : 0x60000000), box);
            }
            dc.DrawRectangle(null, Pen(now ? (Blink(3) ? Yellow : Orange) : 0xFF404060, now ? 4 : 2), box, 8, 8);
            Text(dc, Short(Circuit.Boxers[i].Name), new Point(box.X + 74, box.Bottom + 14), 20, now ? Yellow : Dim, center: true);
            if (beaten)
            {
                dc.DrawLine(Pen(0xC0FF3030, 6), new Point(box.X + 20, box.Y + 20), new Point(box.Right - 20, box.Bottom - 20));
                dc.DrawLine(Pen(0xC0FF3030, 6), new Point(box.Right - 20, box.Y + 20), new Point(box.X + 20, box.Bottom - 20));
            }
        }
        if (g.State == GameState.Playing && g.PhaseTime > 1.0 && Blink(2)) Text(dc, "PUNCH TO FIGHT", new Point(256, 616 - 30), 26, Yellow, center: true);
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawArena(dc, null, 0.25);
        // The champion in the spotlight, and you in front of him.
        for (int i = 0; i < 6; i++) dc.DrawEllipse(Brush(0x10FFF0C0), null, new Point(cx, 430), 300 - i * 40, 330 - i * 40);
        var champ = new Opponent { P = Circuit.Boxers[^1], S = Stance.Guard, GuardHigh = true, T = _clock };
        DrawOpponentAt(dc, champ, cx + Math.Sin(_clock * 2) * 8, 548, 0.86);
        var you = new Boxer { Act = (int)(_clock / 1.3) % 3 == 2 ? Act.Punch : Act.Idle, T = _clock % 1.3 * 0.6, Dur = Sim.PunchDur, Side = (int)(_clock / 3.9) % 2 == 0 ? 1 : -1, High = true };
        DrawBoxer(dc, you, null, 70);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 0, Field.Width, 204));
        string title = "PUNCH-OUT!!";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 33 + 33 + i * 66, ty = 120 + Math.Sin(_clock * 3 + i * 0.5) * 5;
            Shout(dc, title[i].ToString(), new Point(tx, ty), 130, i < 5 ? Yellow : 0xFFFF5A3A, 0xFF801010, 6);
        }
        Text(dc, "A V A", new Point(cx, 40), 40, Cyan, center: true);
        Text(dc, "IN THE STYLE OF NINTENDO'S 1984 ARCADE GAME", new Point(cx, 188), 24, White, center: true);
        if (Blink(2)) Shout(dc, "PRESS ENTER TO START", new Point(cx, 600), 48, Yellow, 0xFF101010, 4);
        dc.FillRectangle(Brush(0xE0000000), new Rect(0, 640, Field.Width, 128));
        Text(dc, "SIX CONTENDERS STAND BETWEEN YOU AND THE TITLE. WATCH FOR EACH ONE'S TELL, DODGE OR", new Point(cx, 660), 22, White, center: true);
        Text(dc, "BLOCK, AND COUNTER WHILE HE'S OPEN. FILL THE POWER METER FOR THE KO PUNCH.", new Point(cx, 686), 22, White, center: true);
        Text(dc, "Z/X: LEFT/RIGHT PUNCH (HOLD UP: TO THE FACE)   LEFT/RIGHT: DODGE   DOWN: BLOCK   SPACE: KO PUNCH", new Point(cx, 720), 19, 0xFFE0D0B0, center: true);
        Text(dc, "P: PAUSE   M: SOUND     MUSIC: THE ENTERTAINER (SCOTT JOPLIN, 1902)", new Point(cx, 748), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawArena(dc, null, 0.55);
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 380, 150, 760, 330), 14);
        Shout(dc, "GAME OVER", new Point(cx, 230), 100, Blink(3) ? Red : White, 0xFF101010, 6);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 320), 46, Yellow, center: true);
        Text(dc, $"WON {g.Wins}  LOST {g.Losses}", new Point(cx, 374), 32, Cyan, center: true);
        Text(dc, $"LAST BEATEN BY {g.Opponent.Name}", new Point(cx, 424), 28, White, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8000000), new Rect(cx - 330, 60, 660, 660), 14);
        dc.DrawRectangle(null, Pen(0xFF404070, 3), new Rect(cx - 330, 60, 660, 660), 14, 14);
        Shout(dc, "WORLD RANKINGS", new Point(cx, 112), 64, Yellow, 0xFF901010, 5);
        Text(dc, "RANK NAME    SCORE   WINS", new Point(cx, 180), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && Blink(4);
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 226 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD8000000), new Rect(cx - 330, 140, 660, 460), 14);
        Shout(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, 0xFF901010, 5);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && Blink(3);
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
