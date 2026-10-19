using Avalonia;
using Avalonia.Media;
using AVAPunchOut.Engine;

namespace AVAPunchOut.Views;

/// <summary>The figures: him, big and facing you; you, from behind, a see-through outline; and the referee.</summary>
public sealed partial class GameCanvas
{
    private enum Face { Calm, Mean, Hurt, Dazed, Wink, Grin, Shout, Out }

    /// <summary>Where his gloves (left and right of the screen), elbows, head and body are, in his own frame: the waist
    /// at the origin, the head 250 up.</summary>
    private struct Pose
    {
        public Point L, R;
        public double Ls, Rs;
        public double Dx, Dy, HeadX, HeadY, Tilt, Lunge;
        public Face Face;
        /// <summary>The punching glove (-1, +1), flashing in the last of the wind-up; 0 for none.</summary>
        public int Flash;
        public double Glow;
    }

    private static Pose GuardPose(bool high) => high
        ? new Pose { L = new(-56, -198), R = new(56, -198), Ls = 1, Rs = 1, Face = Face.Calm }
        : new Pose { L = new(-64, -92), R = new(64, -92), Ls = 1, Rs = 1, Face = Face.Calm };

    private static Pose Mix(Pose a, Pose b, double t) => new()
    {
        L = Lerp(a.L, b.L, t), R = Lerp(a.R, b.R, t), Ls = a.Ls + (b.Ls - a.Ls) * t, Rs = a.Rs + (b.Rs - a.Rs) * t,
        Dx = a.Dx + (b.Dx - a.Dx) * t, Dy = a.Dy + (b.Dy - a.Dy) * t, HeadX = a.HeadX + (b.HeadX - a.HeadX) * t,
        HeadY = a.HeadY + (b.HeadY - a.HeadY) * t, Tilt = a.Tilt + (b.Tilt - a.Tilt) * t, Lunge = a.Lunge + (b.Lunge - a.Lunge) * t,
        Face = t < 0.5 ? a.Face : b.Face,
    };

    private static double Ease(double k) => 1 - (1 - k) * (1 - k);

    /// <summary>The glove a move comes with: its side, or for one down the middle, his right (the left of the screen).</summary>
    private static int GloveOf(Move m, int side) => side != 0 ? side : m.Kind is Kind.Jab or Kind.Body ? 1 : -1;

    private static void SetGlove(ref Pose p, int g, Point at, double scale)
    {
        if (g < 0) { p.L = at; p.Ls = scale; }
        else { p.R = at; p.Rs = scale; }
    }

    /// <summary>The wind-up: each kind of punch drawn back its own way, and each man's own tell on top.</summary>
    private static Pose WindUp(Opponent h, Move m, double k, double t)
    {
        var p = GuardPose(true);
        p.Face = Face.Mean;
        double e = Ease(k), W = h.P.Look.Width;
        int g = GloveOf(m, h.Side);
        var look = h.P.Look;
        if (m.Special && look.Tell == Tell.Raise)
        {
            // Both fists up over his head, shaking.
            p.L = Lerp(p.L, new Point(-84, -330), e);
            p.R = Lerp(p.R, new Point(84, -330), e);
            p.Dy = -12 * e;
            p.Dx = Math.Sin(t * 70) * 4 * e;
            p.Face = Face.Shout;
        }
        else if (m.Kind == Kind.Upper)
        {
            p.Dy = (m.Special ? 46 : 26) * e;
            SetGlove(ref p, g, Lerp(GloveAt(p, g), new Point(g * 40, 6), e), 1);
            if (m.Special) SetGlove(ref p, -g, Lerp(GloveAt(p, -g), new Point(-g * 52, -10), e), 1);
        }
        else if (m.Kind == Kind.Smash)
        {
            p.L = Lerp(p.L, new Point(-190 * W, -108), e);
            p.R = Lerp(p.R, new Point(190 * W, -108), e);
            p.Dy = -8 * e;
            p.HeadY = -8 * e;
        }
        else if (m.Kind == Kind.Haymaker)
        {
            SetGlove(ref p, g, Lerp(GloveAt(p, g), new Point(g * 236 * W, -300), e), 1 - 0.22 * e);
            p.HeadX = g * 18 * e;
            p.Dx = g * 22 * e;
        }
        else if (m.Kind == Kind.Body)
            SetGlove(ref p, g, Lerp(GloveAt(p, g), new Point(g * 150 * W, -50), e), 1 - 0.12 * e);
        else
            SetGlove(ref p, g, Lerp(GloveAt(p, g), new Point(g * 168 * W, -286), e), 1 - 0.12 * e);
        if (look.Tell == Tell.Wink) p.Face = Face.Wink;
        if (k > 0.6) p.Flash = g;
        if (look.Tell == Tell.Dim && m.Special) p.Glow = e;
        return p;
    }

    private static Point GloveAt(Pose p, int g) => g < 0 ? p.L : p.R;

    /// <summary>The swing: the glove coming at you, getting bigger.</summary>
    private static Pose Swing(Opponent h, Move m, double k)
    {
        var p = WindUp(h, m, 1, 0);
        p.Flash = 0;
        p.Dx = 0;
        double e = Ease(k);
        int g = GloveOf(m, h.Side);
        p.Face = Face.Shout;
        if (m.Special && h.P.Look.Tell == Tell.Raise)
        {
            p.L = Lerp(p.L, new Point(-48, 80), e);
            p.R = Lerp(p.R, new Point(48, 80), e);
            p.Ls = p.Rs = 1 + e;
            p.Dy = 30 * e;
            return p;
        }
        if (m.Kind == Kind.Smash)
        {
            p.L = Lerp(p.L, new Point(-80, 150), e);
            p.R = Lerp(p.R, new Point(80, 150), e);
            p.Ls = p.Rs = 1 + 0.75 * e;
            p.Lunge = 0.14 * e;
            p.Dy = 40 * e;
            return p;
        }
        var target = m.Low ? new Point(g * 50, 180) : new Point(m.Kind == Kind.Upper ? g * 10 : g * 58, 70);
        SetGlove(ref p, g, Lerp(GloveAt(p, g), target, e), GloveScale(p, g) + (2.1 - GloveScale(p, g)) * e);
        p.Dy *= 1 - e;
        p.Lunge = 0.05 * e;
        return p;
    }

    private static double GloveScale(Pose p, int g) => g < 0 ? p.Ls : p.Rs;

    /// <summary>Showing off, each his own way.</summary>
    private static Pose TauntPose(Opponent h, double t)
    {
        var p = GuardPose(false);
        p.Face = Face.Grin;
        double W = h.P.Look.Width;
        switch (h.P.Look.Tell)
        {
            case Tell.Blush:
                p.R = new Point(22, -216);
                p.L = new Point(-46, -126);
                break;
            case Tell.Glint:
                p.Dx = Math.Sin(t * 10) * 24;
                p.L = new Point(-72, -76 + Math.Abs(Math.Sin(t * 10)) * -16);
                p.R = new Point(72, -76 + Math.Abs(Math.Cos(t * 10)) * -16);
                break;
            case Tell.Raise:
                p.L = new Point(-180 * W, -300);
                p.R = new Point(180 * W, -300);
                break;
            case Tell.Wink:
                double s = (Math.Sin(t * 5) + 1) / 2;
                p.R = new Point(26 + 90 * s, -222 - 30 * s);
                p.Face = Face.Wink;
                break;
            case Tell.Wobble:
                p.L = new Point(-50, -46 + Math.Sin(t * 14) * 9);
                p.R = new Point(50, -46 - Math.Sin(t * 14) * 9);
                break;
            case Tell.Dim:
                p.R = new Point(76, -150 + Math.Sin(t * 8) * 12);
                p.Rs = 1.2;
                p.L = new Point(-56, -198);
                break;
        }
        return p;
    }

    private Pose PoseOf(Opponent h, double t)
    {
        double k = h.Dur > 0 ? Math.Clamp(h.T / h.Dur, 0, 1) : 1;
        double W = h.P.Look.Width;
        switch (h.S)
        {
            case Stance.Guard:
            {
                var p = GuardPose(h.GuardHigh);
                p.Dy = Math.Sin(t * 5) * 3;
                p.L = new Point(p.L.X, p.L.Y + Math.Sin(t * 5 + 1) * 3);
                p.R = new Point(p.R.X, p.R.Y + Math.Sin(t * 5 + 2) * 3);
                return p;
            }
            case Stance.Tell when h.Move is { } m:
                return WindUp(h, m, h.Hit > 0 ? Math.Min(1, k * 1.5) : k, t);
            case Stance.Strike when h.Move is { } m:
                return Swing(h, m, k);
            case Stance.Recover:
                return Mix(new Pose { L = new(-90, -60), R = new(90, -60), Ls = 1.2, Rs = 1.2, Face = Face.Mean }, GuardPose(true), k);
            case Stance.Open:
            {
                var p = new Pose { L = new(-86, -28), R = new(86, -28), Ls = 1, Rs = 1, Face = Face.Dazed };
                p.Tilt = Math.Sin(t * 3) * 0.16;
                p.HeadX = Math.Sin(t * 3) * 8;
                p.Dx = Math.Sin(t * 1.5) * 10;
                return p;
            }
            case Stance.Hurt:
            {
                var p = new Pose { L = new(-104, -150), R = new(104, -150), Ls = 1, Rs = 1, Face = Face.Hurt };
                double j = 1 - k;
                p.HeadX = -h.Flinch * 30 * j;
                p.Tilt = -h.Flinch * 0.2 * j;
                if (!h.FlinchHigh) { p.Dy = 18 * j; p.HeadY = 10 * j; }
                return p;
            }
            case Stance.Cover:
                return new Pose { L = new(-30, -236), R = new(30, -236), Ls = 1.15, Rs = 1.15, Face = Face.Mean, Dy = 8 };
            case Stance.Taunt:
                return TauntPose(h, t);
            case Stance.Rise:
            {
                var p = GuardPose(false);
                p.Face = Face.Dazed;
                p.Dy = (1 - Ease(k)) * 200;
                return p;
            }
            case Stance.Corner:
            {
                var p = GuardPose(false);
                p.Face = Face.Grin;
                p.L = new Point(-110 * W, -60);
                p.R = new Point(110 * W, -60);
                return p;
            }
            case Stance.Cheer:
            {
                var p = new Pose { L = new(-96, -400), R = new(96, -400), Ls = 1, Rs = 1, Face = Face.Shout };
                p.Dy = -Math.Abs(Math.Sin(t * 6)) * 12;
                return p;
            }
            case Stance.Down or Stance.Out:
                return new Pose { L = new(-180 * W, -150), R = new(180 * W, -110), Ls = 1, Rs = 1, Face = h.S == Stance.Out ? Face.Out : Face.Dazed };
        }
        return GuardPose(true);
    }

    /// <summary>Him, standing with his waist at (x, y), at a scale.</summary>
    private void DrawOpponentAt(DrawingContext dc, Opponent h, double x, double y, double scale, bool portrait = false)
    {
        using (dc.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
            DrawOpponent(dc, h, PoseOf(h, portrait ? h.T : _clock), portrait ? h.T : _clock);
    }

    /// <summary>Him flat on the canvas, seen from his feet.</summary>
    private void DrawOpponentDown(DrawingContext dc, Opponent h)
    {
        var m = Matrix.CreateRotation(Math.PI / 2) * Matrix.CreateScale(0.8, 0.5) * Matrix.CreateTranslation(450, 470);
        dc.DrawEllipse(Brush(0x40000000), null, new Point(470, 482), 290, 70);
        using (dc.PushTransform(m))
            DrawOpponent(dc, h, PoseOf(h, _clock), _clock);
        if (h.S == Stance.Down)
            for (int i = 0; i < 4; i++)
            {
                double a = _clock * 4 + i * Math.PI / 2;
                Star(dc, 660 + Math.Cos(a) * 46, 430 + Math.Sin(a) * 14, 11, 5, Yellow, 0.45, a);
            }
    }

    private void DrawOpponent(DrawingContext dc, Opponent h, Pose p, double t)
    {
        var look = h.P.Look;
        double W = look.Width, B = look.Belly;
        uint skin = look.Skin, dark = Shade(skin, -0.45), edge = Shade(skin, -0.6);
        // A wobbling belly is Big Biscuit's tell.
        if (look.Tell == Tell.Wobble && h.S == Stance.Tell) B += Math.Sin(t * 26) * 0.25;
        // The legs, behind everything.
        foreach (int s in new[] { -1, 1 })
        {
            Blob(dc, Shade(skin, -0.12), edge, 3, new Point(s * 12, 70), new Point(s * 98 * W, 70), new Point(s * 92 * W, 160), new Point(s * 76 * W, 228),
                new Point(s * 82 * W, 280), new Point(s * 70 * W, 330), new Point(s * 34, 330), new Point(s * 30, 250), new Point(s * 24, 160));
            dc.DrawRectangle(Brush(0xFF1A1A22), Pen(0xFF000000, 2), new Rect(s > 0 ? 28 : -72 * W - 6, 286, 72 * W - 22, 60), 10, 10);
            for (int i = 0; i < 3; i++) dc.DrawLine(Pen(look.Trim, 3), new Point(s * (36 + 8 * W), 296 + i * 12), new Point(s * (60 * W - 4), 296 + i * 12));
        }
        using (dc.PushTransform(Matrix.CreateTranslation(p.Dx, p.Dy) * Matrix.CreateScale(1 + p.Lunge, 1 + p.Lunge)))
        {
            // The trunks, and the body above them.
            double tw = 92 * W + 30 * B;
            Blob(dc, look.Trunks, Shade(look.Trunks, -0.5), 3, new Point(-tw, -10), new Point(tw, -10), new Point(tw + 10, 40), new Point(tw + 6, 78),
                new Point(14, 82), new Point(0, 58), new Point(-14, 82), new Point(-tw - 6, 78), new Point(-tw - 10, 40));
            dc.DrawLine(Pen(look.Trim, 6), new Point(-tw - 8, 30), new Point(-tw + 2, 78));
            dc.DrawLine(Pen(look.Trim, 6), new Point(tw + 8, 30), new Point(tw - 2, 78));
            var torso = new[]
            {
                new Point(-104 * W, -170), new Point(-58 * W, -190), new Point(0, -194), new Point(58 * W, -190), new Point(104 * W, -170),
                new Point(122 * W, -140), new Point(104 * W + 22 * B, -80), new Point(90 * W + 42 * B, -24), new Point(86 * W + 30 * B, 2),
                new Point(0, 8), new Point(-86 * W - 30 * B, 2), new Point(-90 * W - 42 * B, -24), new Point(-104 * W - 22 * B, -80), new Point(-122 * W, -140),
            };
            Blob(dc, skin, edge, 3, torso);
            // Chest and stomach.
            Curve(dc, dark, 3, new Point(-84 * W, -128), new Point(-46 * W, -108), new Point(-8, -116));
            Curve(dc, dark, 3, new Point(84 * W, -128), new Point(46 * W, -108), new Point(8, -116));
            if (B > 0)
            {
                dc.DrawEllipse(Brush(Shade(skin, 0.08)), Pen(dark, 2), new Point(0, -40), 80 * W * (0.8 + 0.2 * B), 50 + 8 * B);
                dc.FillEllipse(0, -30, 4, dark);
            }
            else
            {
                dc.DrawLine(Pen(dark, 2), new Point(0, -104), new Point(0, -14));
                for (int i = 0; i < 3; i++)
                {
                    double ay = -88 + i * 26;
                    dc.DrawLine(Pen(dark, 2), new Point(-30, ay), new Point(-8, ay + 3));
                    dc.DrawLine(Pen(dark, 2), new Point(30, ay), new Point(8, ay + 3));
                }
            }
            dc.DrawRectangle(Brush(look.Trim), Pen(Shade(look.Trim, -0.4), 2), new Rect(-tw - 4, -16, tw * 2 + 8, 22), 6, 6);
            if (look.Sash != 0)
            {
                // A champion's belt (gold) or a sash tied at the hip.
                if (look.Tell == Tell.Dim)
                {
                    dc.DrawRectangle(Brush(look.Sash), Pen(0xFF7A5A10, 2), new Rect(-tw - 6, -22, tw * 2 + 12, 34), 6, 6);
                    dc.DrawEllipse(Brush(0xFFFFF0A0), Pen(0xFF7A5A10, 3), new Point(0, -5), 40, 26);
                    Star(dc, 0, -5, 16, 5, 0xFFE03A2E, 0.45);
                }
                else
                {
                    Poly(dc, look.Sash, Shade(look.Sash, -0.4), 2, new Point(tw - 10, -14), new Point(tw + 26, 30), new Point(tw + 12, 34), new Point(tw - 16, 4));
                    Poly(dc, look.Sash, Shade(look.Sash, -0.4), 2, new Point(tw - 12, -10), new Point(tw + 6, 44), new Point(tw - 8, 46), new Point(tw - 22, 0));
                }
            }
            // The arms, then the head, then the gloves in front.
            var sl = new Point(-104 * W, -160);
            var sr = new Point(104 * W, -160);
            Arm(dc, sl, p.L, -1, skin, edge, W);
            Arm(dc, sr, p.R, 1, skin, edge, W);
            var hc = new Point(p.HeadX, -252 + p.HeadY);
            using (dc.PushTransform(Matrix.CreateTranslation(-hc.X, -hc.Y) * Matrix.CreateRotation(p.Tilt) * Matrix.CreateTranslation(hc.X, hc.Y)))
                DrawHead(dc, h, look, hc, p, t);
            bool lFront = p.Ls >= p.Rs;
            if (lFront) { DrawGlove(dc, h, look, p.R, p.Rs, -1, p.Flash == 1, t); DrawGlove(dc, h, look, p.L, p.Ls, 1, p.Flash == -1, t); }
            else { DrawGlove(dc, h, look, p.L, p.Ls, 1, p.Flash == -1, t); DrawGlove(dc, h, look, p.R, p.Rs, -1, p.Flash == 1, t); }
            // Big Gunnar's tell: he flashes as he raises his fists.
            if (look.Tell == Tell.Raise && h.S == Stance.Tell && h.Move is { Special: true } && (int)(t * 12) % 2 == 0)
                dc.DrawGeometry(Brush(0x50FFFFFF), Pen(0xC0FFFFFF, 5), Smooth(torso));
            if (h.S == Stance.Taunt && look.Tell == Tell.Blush) DrawTeacup(dc, p.R);
            if (h.S == Stance.Taunt && look.Tell == Tell.Wink)
            {
                double k = (t * 0.8) % 1;
                Heart(dc, p.R.X + 30 + k * 60, p.R.Y - 30 - k * 120, 16 + k * 10, Alpha(0xFFFF4A7A, 1 - k));
            }
        }
    }

    private static void Arm(DrawingContext dc, Point shoulder, Point glove, int side, uint skin, uint edge, double W)
    {
        var mid = Lerp(shoulder, glove, 0.5);
        double reach = Math.Max(0, 1 - Math.Abs(glove.Y - shoulder.Y) / 260);
        var elbow = new Point(mid.X + side * (34 + 20 * reach) * W, mid.Y + 58);
        if (glove.Y > shoulder.Y + 120) elbow = new Point(mid.X + side * 46 * W, mid.Y);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(shoulder, false);
            c.LineTo(elbow);
            c.LineTo(glove);
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, Pen(edge, 40 * W + 6), g);
        dc.DrawGeometry(null, Pen(skin, 40 * W), g);
        dc.DrawLine(Pen(Shade(skin, 0.18), 10 * W), Lerp(shoulder, elbow, 0.2), Lerp(shoulder, elbow, 0.7));
    }

    private static void DrawGlove(DrawingContext dc, Opponent h, Look look, Point at, double scale, int thumb, bool flash, double t)
    {
        double r = 42 * scale;
        uint colour = flash && (int)(t * 16) % 2 == 0 ? 0xFFFFFFFF : look.Gloves;
        Glove(dc, at, r, colour, thumb);
        // Rocket's tell: a glint off his glove.
        if (flash && look.Tell == Tell.Glint) Star(dc, at.X - r * 0.4, at.Y - r * 0.5, r * 0.9, 4, 0xFFFFF6B0, 0.2, t * 6);
    }

    private static void Glove(DrawingContext dc, Point at, double r, uint colour, int thumb)
    {
        uint dark = Shade(colour, -0.5);
        dc.DrawEllipse(Brush(0xFFF4F0E8), Pen(dark, 2), new Point(at.X, at.Y + r * 0.92), r * 0.62, r * 0.3);
        dc.DrawEllipse(Brush(colour), Pen(dark, Math.Max(2, r * 0.07)), at, r, r * 1.08);
        dc.DrawEllipse(Brush(Shade(colour, -0.12)), Pen(dark, Math.Max(1.5, r * 0.05)), new Point(at.X + thumb * r * 0.64, at.Y + r * 0.18), r * 0.34, r * 0.48);
        dc.DrawEllipse(Brush(0x70FFFFFF), null, new Point(at.X - r * 0.3, at.Y - r * 0.42), r * 0.34, r * 0.2);
        dc.DrawLine(Pen(dark, Math.Max(1.5, r * 0.05)), new Point(at.X - r * 0.5, at.Y + r * 0.62), new Point(at.X + r * 0.5, at.Y + r * 0.62));
    }

    private static void DrawTeacup(DrawingContext dc, Point glove)
    {
        var c = new Point(glove.X + 30, glove.Y - 34);
        Poly(dc, 0xFFFFFFFF, 0xFF8080A0, 2, new Point(c.X - 18, c.Y - 12), new Point(c.X + 18, c.Y - 12), new Point(c.X + 12, c.Y + 12), new Point(c.X - 12, c.Y + 12));
        dc.DrawEllipse(null, Pen(0xFF8080A0, 3), new Point(c.X + 20, c.Y), 7, 7);
        dc.DrawLine(Pen(0xFFE87AA8, 3), new Point(c.X - 14, c.Y - 4), new Point(c.X + 14, c.Y - 4));
        for (int i = 0; i < 2; i++) Curve(dc, 0x80FFFFFF, 2, new Point(c.X - 6 + i * 10, c.Y - 16), new Point(c.X - 10 + i * 10, c.Y - 26), new Point(c.X - 4 + i * 10, c.Y - 36));
    }

    private static void Heart(DrawingContext dc, double x, double y, double r, uint colour)
    {
        dc.FillEllipse(x - r * 0.5, y, r * 0.55, colour);
        dc.FillEllipse(x + r * 0.5, y, r * 0.55, colour);
        Poly(dc, colour, null, 0, new Point(x - r * 1.02, y + r * 0.15), new Point(x + r * 1.02, y + r * 0.15), new Point(x, y + r * 1.2));
    }

    private void DrawHead(DrawingContext dc, Opponent h, Look look, Point c, Pose p, double t)
    {
        double wh = Math.Sqrt(look.Width);
        uint skin = look.Skin, edge = Shade(skin, -0.6), dark = Shade(skin, -0.4);
        Point P(double x, double y) => new(c.X + x * wh, c.Y + y);
        // Hair that hangs behind the head.
        if (look.Hair == Hair.Braids)
        {
            Blob(dc, look.HairColour, Shade(look.HairColour, -0.45), 3, P(-50, -30), P(-62, 40), P(-48, 96), P(-30, 60), P(30, 60), P(48, 96), P(62, 40), P(50, -30));
        }
        if (look.Hair == Hair.Bandana)
        {
            double f = Math.Sin(t * 9) * 8;
            Poly(dc, look.Sash, Shade(look.Sash, -0.4), 2, P(40, -30), P(96, -8 + f), P(88, 6 + f), P(40, -16));
            Poly(dc, look.Sash, Shade(look.Sash, -0.4), 2, P(40, -24), P(84, 20 - f), P(74, 30 - f), P(38, -10));
        }
        // Neck, ears, head.
        dc.DrawRectangle(Brush(skin), Pen(edge, 3), new Rect(c.X - 30 * wh, c.Y + 30, 60 * wh, 40));
        dc.DrawEllipse(Brush(skin), Pen(edge, 3), P(-47, 2), 10, 15);
        dc.DrawEllipse(Brush(skin), Pen(edge, 3), P(47, 2), 10, 15);
        Blob(dc, skin, edge, 3, P(0, -58), P(36, -52), P(49, -22), P(48, 10), P(40, 38), P(20, 54), P(0, 58), P(-20, 54), P(-40, 38), P(-48, 10), P(-49, -22), P(-36, -52));
        bool tell = h.S == Stance.Tell && h.Move != null;
        // Teacup Terry's tell: he blushes, bright pink.
        if (look.Tell == Tell.Blush && (tell || h.S == Stance.Taunt))
        {
            double a = tell ? 0.45 + 0.35 * Math.Sin(t * 22) : 0.35;
            dc.FillEllipse(c.X - 26 * wh, c.Y + 18, 13, Alpha(0xFFFF3A6A, a));
            dc.FillEllipse(c.X + 26 * wh, c.Y + 18, 13, Alpha(0xFFFF3A6A, a));
            if (tell) Blob(dc, Alpha(0xFFFF5A8A, a * 0.45), null, 0, P(0, -58), P(49, -22), P(40, 38), P(0, 58), P(-40, 38), P(-49, -22));
        }
        DrawFace(dc, look, c, wh, p, t, dark);
        // Hair on top.
        uint hair = look.HairColour, hairEdge = Shade(hair, -0.5);
        switch (look.Hair)
        {
            case Hair.Parted:
                Blob(dc, hair, hairEdge, 2, P(-49, -14), P(-46, -44), P(-22, -62), P(10, -64), P(40, -50), P(50, -16), P(36, -30), P(4, -42), P(-14, -40), P(-34, -28));
                dc.DrawLine(Pen(hairEdge, 2), P(-14, -40), P(-20, -60));
                break;
            case Hair.Bandana:
                Blob(dc, hair, hairEdge, 2, P(-49, -14), P(-44, -48), P(0, -64), P(44, -48), P(49, -14), P(30, -30), P(-30, -30));
                Curve(dc, Shade(look.Sash, -0.4), 16, P(-49, -24), P(0, -36), P(49, -24));
                Curve(dc, look.Sash, 12, P(-49, -24), P(0, -36), P(49, -24));
                break;
            case Hair.Braids:
                Blob(dc, hair, hairEdge, 2, P(-50, -10), P(-46, -46), P(0, -64), P(46, -46), P(50, -10), P(34, -34), P(0, -42), P(-34, -34));
                break;
            case Hair.Pompadour:
                Blob(dc, hair, hairEdge, 2, P(-49, -12), P(-48, -50), P(-26, -86), P(16, -100), P(52, -84), P(56, -50), P(49, -14), P(30, -40), P(-10, -46), P(-34, -34));
                Curve(dc, 0x90FFFFFF, 4, P(-20, -76), P(10, -90), P(36, -82));
                break;
            case Hair.Shaved:
                dc.DrawEllipse(Brush(0x40FFFFFF), null, P(-14, -42), 18, 9);
                break;
            case Hair.FlatTop:
                Blob(dc, hair, hairEdge, 2, P(-48, -16), P(-50, -58), P(-42, -78), P(42, -78), P(50, -58), P(48, -16), P(30, -36), P(-30, -36));
                dc.DrawRectangle(Brush(0xFFB0B0B8), null, new Rect(c.X + 8 * wh, c.Y - 76, 12 * wh, 38));
                for (int i = 0; i < 5; i++) dc.DrawLine(Pen(Shade(hair, 0.15), 2), P(-36 + i * 16, -74), P(-36 + i * 16, -50));
                break;
        }
        // Beards below the mouth.
        if (look.Beard)
        {
            if (look.Hair == Hair.Braids)
            {
                Blob(dc, hair, hairEdge, 2, P(-44, 14), P(-36, 46), P(-14, 70), P(0, 112), P(14, 70), P(36, 46), P(44, 14), P(24, 40), P(0, 44), P(-24, 40));
                for (int i = 0; i < 4; i++) dc.DrawLine(Pen(hairEdge, 2), P(-8, 64 + i * 11), P(8, 70 + i * 11));
                dc.FillEllipse(c.X, c.Y + 110, 6, 0xFFC09040);
                Curve(dc, hair, 6, P(-26, 30), P(-10, 26), P(0, 30), P(10, 26), P(26, 30));
            }
            else if (look.Hair == Hair.Shaved)
            {
                foreach (int s in new[] { -1, 1 })
                    Blob(dc, hair, hairEdge, 2, P(s * 48, -10), P(s * 48, 24), P(s * 36, 48), P(s * 22, 40), P(s * 34, 10), P(s * 40, -10));
            }
            else Blob(dc, hair, hairEdge, 2, P(-16, 40), P(0, 66), P(16, 40), P(8, 48), P(-8, 48));
        }
        // The champion's tell: the lights go down and his eyes burn.
        if (p.Glow > 0)
        {
            foreach (int s in new[] { -1, 1 })
            {
                dc.FillEllipse(c.X + s * 18 * wh, c.Y - 4, 22 * p.Glow, Alpha(0xFFFFE070, 0.35 * p.Glow));
                dc.FillEllipse(c.X + s * 18 * wh, c.Y - 4, 7, Alpha(0xFFFFFFE0, p.Glow));
            }
        }
        if (h.S == Stance.Open)
            for (int i = 0; i < 4; i++)
            {
                double a = t * 5 + i * Math.PI / 2;
                Star(dc, c.X + Math.Cos(a) * 58, c.Y - 76 + Math.Sin(a) * 14, 12, 5, Yellow, 0.45, a);
            }
        if (h.S == Stance.Hurt)
            for (int i = 0; i < 3; i++)
                dc.FillEllipse(c.X - h.Flinch * (52 + i * 16), c.Y - 30 + i * 22, 5, 0xC0B0E0FF);
    }

    private static void DrawFace(DrawingContext dc, Look look, Point c, double wh, Pose p, double t, uint dark)
    {
        Point P(double x, double y) => new(c.X + x * wh, c.Y + y);
        var ink = Pen(0xFF181010, 3);
        void Eye(int s, bool open)
        {
            if (!open) { Curve(dc, 0xFF181010, 3, P(s * 26, -4), P(s * 18, 2), P(s * 10, -4)); return; }
            dc.DrawEllipse(Brush(0xFFF8F8F0), Pen(dark, 2), P(s * 18, -6), 10, 7);
            dc.FillEllipse(c.X + s * 18 * wh - s * 2, c.Y - 6, 4, 0xFF201810);
        }
        switch (p.Face)
        {
            case Face.Hurt:
                foreach (int s in new[] { -1, 1 })
                {
                    dc.DrawLine(ink, P(s * 26, -12), P(s * 12, -5));
                    dc.DrawLine(ink, P(s * 26, 2), P(s * 12, -5));
                }
                dc.DrawEllipse(Brush(0xFF401010), Pen(0xFF181010, 2), P(0, 30), 13, 10);
                dc.DrawRectangle(Brush(0xFFF0F0F0), null, new Rect(c.X - 10 * wh, c.Y + 22, 20 * wh, 6));
                break;
            case Face.Dazed:
                foreach (int s in new[] { -1, 1 })
                {
                    dc.DrawEllipse(Brush(0xFFF8F8F0), Pen(dark, 2), P(s * 18, -6), 10, 9);
                    Curve(dc, 0xFF181010, 2, P(s * 18 - 5, -6), P(s * 18, -11), P(s * 18 + 5, -6), P(s * 18, -2), P(s * 18 - 2, -6));
                }
                dc.DrawEllipse(Brush(0xFF401010), Pen(0xFF181010, 2), P(0, 32), 10, 13);
                dc.FillEllipse(c.X + 4, c.Y + 42, 6, 0xFFE06070);
                break;
            case Face.Out:
                foreach (int s in new[] { -1, 1 })
                {
                    dc.DrawLine(ink, P(s * 24, -12), P(s * 12, 0));
                    dc.DrawLine(ink, P(s * 12, -12), P(s * 24, 0));
                }
                dc.DrawLine(ink, P(-12, 30), P(12, 30));
                dc.FillEllipse(c.X + 6, c.Y + 36, 7, 0xFFE06070);
                break;
            default:
                Eye(-1, true);
                Eye(1, p.Face != Face.Wink);
                if (p.Face == Face.Wink) Star(dc, c.X + 34 * wh, c.Y - 18, 12, 4, 0xFFFFFFFF, 0.25, t * 4);
                bool mean = p.Face is Face.Mean or Face.Shout;
                foreach (int s in new[] { -1, 1 })
                    dc.DrawLine(Pen(Shade(look.HairColour, -0.2), 5), P(s * 30, mean ? -22 : -20), P(s * 8, mean ? -14 : -20));
                if (p.Face == Face.Shout)
                {
                    dc.DrawEllipse(Brush(0xFF401010), Pen(0xFF181010, 2), P(0, 30), 15, 11);
                    dc.DrawRectangle(Brush(0xFFF0F0F0), null, new Rect(c.X - 12 * wh, c.Y + 22, 24 * wh, 6));
                }
                else if (p.Face == Face.Grin)
                {
                    Poly(dc, 0xFFF8F8F0, 0xFF181010, 2, P(-20, 24), P(20, 24), P(12, 36), P(-12, 36));
                }
                else Curve(dc, 0xFF181010, 3, P(-14, mean ? 32 : 28), P(0, mean ? 28 : 31), P(14, mean ? 32 : 28));
                break;
        }
        // The nose.
        Curve(dc, dark, 3, P(-6, 2), P(-8, 14), P(0, 18), P(8, 14), P(6, 2));
        if (look.Moustache)
        {
            if (look.Hair == Hair.Parted)
                Curve(dc, look.HairColour, 5, P(-24, 16), P(-18, 24), P(-6, 20), P(0, 22), P(6, 20), P(18, 24), P(24, 16));
            else dc.DrawLine(Pen(look.HairColour, 3), P(-16, 22), P(16, 22));
        }
    }

    // ───────────────────────────── you ─────────────────────────────

    private const uint Wire = 0xFFE4FCFF, WireGlow = 0x4C30D8FF, WireFill = 0x1430D8FF, Flame = 0xD8FF7A1E, GloveLine = 0xFFFFA040;

    private void DrawYou(DrawingContext dc, World w) => DrawBoxer(dc, w.You, w, 0);

    /// <summary>
    /// You, from behind: a see-through outline, so he can be seen through you. Your hair's a flame; your gloves
    /// orange. A punch reaches up to his face or his body, the glove getting smaller as it goes; a dodge takes you
    /// aside; a block brings both gloves up in front of your face.
    /// </summary>
    private void DrawBoxer(DrawingContext dc, Boxer y, World? w, double lift)
    {
        double k = y.Dur > 0 ? Math.Clamp(y.T / y.Dur, 0, 1) : 0;
        double ox = 512, oy = 548 + lift;
        uint line = Wire;
        double lean = 0;
        var gl = new Point(-96, 8);
        var gr = new Point(96, 8);
        double rl = 44, rr = 44;
        double sway = w?.Him.S == Stance.Guard ? Math.Sin(_clock * 2.1) * 10 : 0;
        switch (y.Act)
        {
            case Act.Idle:
                oy += Math.Sin(_clock * 5) * 3;
                break;
            case Act.Block:
                oy += 16;
                gl = new Point(-36, -4);
                gr = new Point(36, -4);
                rl = rr = 50;
                break;
            case Act.Dodge:
            {
                double s = Math.Sin(Math.PI * k);
                ox += y.Side * 220 * s;
                oy += 26 * s;
                lean = y.Side * 34 * s;
                break;
            }
            case Act.Hurt:
                oy += 34 * (1 - k);
                line = (int)(y.T * 20) % 2 == 0 ? 0xFFFF5A4A : Wire;
                break;
            case Act.Down or Act.Lose:
                oy += Math.Min(1, y.T / 0.35) * 210;
                lean = -40 * Math.Min(1, y.T / 0.35);
                break;
            case Act.Win:
                oy -= Math.Abs(Math.Sin(_clock * 5)) * 12;
                gl = new Point(-128, -176);
                gr = new Point(128, -176);
                break;
            case Act.Punch:
            {
                double ext = k < 0.34 ? Math.Sin(Math.PI / 2 * k / 0.34) : 1 - (k - 0.34) / 0.66;
                var target = new Point(512 + sway + y.Side * (y.High ? 28 : 44), y.High ? 222 : 372);
                var local = new Point(target.X - ox, target.Y - oy);
                if (y.Side < 0) { gl = Lerp(gl, local, ext); rl = 44 - 12 * ext; }
                else { gr = Lerp(gr, local, ext); rr = 44 - 12 * ext; }
                lean = -y.Side * 10 * ext;
                oy -= 8 * ext;
                break;
            }
            case Act.KoPunch:
            {
                double t = y.T;
                var low = new Point(70, 200);
                var top = new Point(512 + sway - ox, 214 - oy);
                if (t < Sim.KoHit * 0.45) gr = Lerp(gr, low, t / (Sim.KoHit * 0.45));
                else if (t < Sim.KoHit) gr = Lerp(low, top, (t - Sim.KoHit * 0.45) / (Sim.KoHit * 0.55));
                else gr = Lerp(top, gr, Math.Clamp((t - Sim.KoHit - 0.12) / 0.26, 0, 1));
                double up = Math.Clamp((gr.Y - 8) / (top.Y - 8), 0, 1);
                rr = 50 - 24 * up;
                oy -= 22 * up;
                lean = -16 * up;
                break;
            }
        }
        using (dc.PushTransform(Matrix.CreateTranslation(ox, oy)))
        {
            // The back: shoulders, the lats tapering to the waist, the spine, the shoulder blades, the trunks' band.
            var back = new[]
            {
                new Point(-30 + lean, 62), new Point(-96, 90), new Point(-146, 104), new Point(-166, 136), new Point(-152, 182), new Point(-118, 226),
                new Point(-110, 260), new Point(110, 260), new Point(118, 226), new Point(152, 182), new Point(166, 136), new Point(146, 104),
                new Point(96, 90), new Point(30 + lean, 62),
            };
            var g = Smooth(back);
            dc.DrawGeometry(Brush(WireFill), Pen(WireGlow, 11), g);
            dc.DrawGeometry(null, Pen(line, 3), g);
            Curve(dc, Alpha(line, 0.55), 2, new Point(lean * 0.6, 76), new Point(lean * 0.3, 160), new Point(0, 236));
            Curve(dc, Alpha(line, 0.45), 2, new Point(-34, 118), new Point(-78, 112), new Point(-96, 150), new Point(-60, 178));
            Curve(dc, Alpha(line, 0.45), 2, new Point(34, 118), new Point(78, 112), new Point(96, 150), new Point(60, 178));
            dc.DrawLine(Pen(GloveLine, 4), new Point(-114, 212), new Point(114, 212));
            dc.DrawLine(Pen(GloveLine, 4), new Point(-112, 226), new Point(112, 226));
            // The arms and gloves.
            YourArm(dc, new Point(-140, 112), gl, -1, line);
            YourArm(dc, new Point(140, 112), gr, 1, line);
            // The head: the back of it, the ears, and the flame of hair.
            var hc = new Point(lean, 0);
            dc.DrawEllipse(Brush(WireFill), Pen(WireGlow, 11), hc, 46, 54);
            dc.DrawEllipse(null, Pen(line, 3), hc, 46, 54);
            dc.DrawLine(Pen(line, 3), new Point(hc.X - 24, 48), new Point(-26 + lean * 0.5, 70));
            dc.DrawLine(Pen(line, 3), new Point(hc.X + 24, 48), new Point(26 + lean * 0.5, 70));
            Curve(dc, line, 3, new Point(hc.X - 44, -6), new Point(hc.X - 56, 4), new Point(hc.X - 46, 18));
            Curve(dc, line, 3, new Point(hc.X + 44, -6), new Point(hc.X + 56, 4), new Point(hc.X + 46, 18));
            var flame = new List<Point>();
            for (int i = 0; i <= 8; i++)
            {
                double a = Math.PI * (1.08 - i * 1.16 / 8);
                double r = i % 2 == 0 ? 50 : 86 + Math.Sin(_clock * 9 + i) * 6;
                flame.Add(new Point(hc.X + Math.Cos(a) * r * (i % 2 == 0 ? 0.98 : 0.62), -6 - Math.Sin(a) * r));
            }
            flame.Add(new Point(hc.X + 30, -20));
            flame.Add(new Point(hc.X, -30));
            flame.Add(new Point(hc.X - 30, -20));
            dc.DrawGeometry(Brush(Flame), Pen(0xFFFFD050, 2.5), Smooth(flame));
            var gloves = rl < rr ? new[] { (gl, rl, -1), (gr, rr, 1) } : new[] { (gr, rr, 1), (gl, rl, -1) };
            foreach (var (at, r, s) in gloves) YourGlove(dc, at, r, s, y.Act == Act.KoPunch && s > 0);
        }
    }

    private static void YourArm(DrawingContext dc, Point shoulder, Point glove, int side, uint line)
    {
        var mid = Lerp(shoulder, glove, 0.5);
        double reach = Math.Clamp((shoulder.Y - glove.Y) / 340, 0, 1);
        var elbow = new Point(mid.X + side * (44 - 30 * reach), mid.Y + 60 - 40 * reach);
        Tube(dc, [shoulder, elbow, glove], [46 - 6 * reach, 36 - 6 * reach, 26 - 4 * reach], line);
    }

    /// <summary>An outlined tube along a line through the points, as wide as given at each: an arm, drawn as wire.</summary>
    private static void Tube(DrawingContext dc, Point[] pts, double[] width, uint line)
    {
        var a = new List<Point>();
        var b = new List<Point>();
        for (int i = 0; i < pts.Length; i++)
        {
            var prev = pts[Math.Max(0, i - 1)];
            var next = pts[Math.Min(pts.Length - 1, i + 1)];
            double dx = next.X - prev.X, dy = next.Y - prev.Y, len = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
            double nx = -dy / len * width[i] / 2, ny = dx / len * width[i] / 2;
            a.Add(new Point(pts[i].X + nx, pts[i].Y + ny));
            b.Add(new Point(pts[i].X - nx, pts[i].Y - ny));
        }
        var outline = a.Concat(Enumerable.Reverse(b)).ToList();
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(outline[0], true);
            foreach (var p in outline.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(Brush(WireFill), Pen(WireGlow, 10), g);
        dc.DrawGeometry(null, Pen(line, 3), g);
    }

    private void YourGlove(DrawingContext dc, Point at, double r, int side, bool power)
    {
        if (power)
        {
            dc.FillEllipse(at.X, at.Y, r * 1.9, 0x40FFE060);
            Star(dc, at.X, at.Y, r * 1.7, 8, 0x90FFF0A0, 0.4, _clock * 8);
        }
        dc.DrawEllipse(Brush(power ? 0xA0FFB040 : 0x50FF6A20), Pen(0x60FF8A20, 10), at, r, r * 1.08);
        dc.DrawEllipse(null, Pen(GloveLine, 3), at, r, r * 1.08);
        dc.DrawEllipse(null, Pen(GloveLine, 2.5), new Point(at.X - side * r * 0.66, at.Y + r * 0.16), r * 0.32, r * 0.46);
        dc.DrawLine(Pen(GloveLine, 2.5), new Point(at.X - r * 0.55, at.Y + r * 0.7), new Point(at.X + r * 0.55, at.Y + r * 0.7));
    }

    // ───────────────────────────── the referee ─────────────────────────────

    /// <summary>The referee: white shirt, bow tie, grey moustache. Counting, his arm comes down with each number.</summary>
    private void DrawReferee(DrawingContext dc, World w, double x, double feet, double scale, bool counting)
    {
        using var _ = dc.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, feet));
        const uint skin = 0xFFF0C8A8, shirt = 0xFFF4F4F8, slacks = 0xFF202028;
        var edge = Pen(0xFF303040, 3);
        Poly(dc, slacks, 0xFF000000, 2, new Point(-32, -150), new Point(-4, -150), new Point(-8, -6), new Point(-30, -6));
        Poly(dc, slacks, 0xFF000000, 2, new Point(4, -150), new Point(32, -150), new Point(30, -6), new Point(8, -6));
        dc.DrawEllipse(Brush(0xFF101010), null, new Point(-22, -2), 18, 8);
        dc.DrawEllipse(Brush(0xFF101010), null, new Point(22, -2), 18, 8);
        Blob(dc, shirt, 0xFF606070, 3, new Point(-46, -266), new Point(46, -266), new Point(52, -200), new Point(38, -146), new Point(-38, -146), new Point(-52, -200));
        dc.DrawLine(Pen(0xFFB0B0C0, 2), new Point(0, -262), new Point(0, -150));
        dc.DrawRectangle(Brush(0xFF101010), null, new Rect(-36, -158, 72, 12));
        // The arms: at his sides, or the counting arm up and coming down on the count.
        double phase = counting ? w.CountT % Sim.CountSecond / Sim.CountSecond : 0;
        double swing = counting ? (phase < 0.25 ? 1 - phase * 4 : Math.Min(1, (phase - 0.25) * 2.5)) : 0;
        var sh = new Point(44, -250);
        var hand = counting ? Lerp(new Point(70, -150), new Point(64, -380), swing) : new Point(56, -150 + Math.Sin(_clock * 2) * 3);
        dc.DrawLine(Pen(0xFF606070, 24), sh, hand);
        dc.DrawLine(Pen(shirt, 20), sh, Lerp(sh, hand, 0.55));
        dc.DrawLine(Pen(skin, 18), Lerp(sh, hand, 0.55), hand);
        dc.FillEllipse(hand.X, hand.Y, 13, skin);
        if (counting) dc.DrawLine(Pen(skin, 7), hand, new Point(hand.X, hand.Y - 24));
        dc.DrawLine(Pen(0xFF606070, 24), new Point(-44, -250), new Point(-56, -150));
        dc.DrawLine(Pen(shirt, 20), new Point(-44, -250), new Point(-50, -196));
        dc.DrawLine(Pen(skin, 18), new Point(-50, -196), new Point(-56, -150));
        dc.FillEllipse(-56, -148, 12, skin);
        // Head: bald, grey at the sides, a big grey moustache, a bow tie.
        dc.DrawRectangle(Brush(skin), edge, new Rect(-14, -284, 28, 22));
        dc.DrawEllipse(Brush(skin), edge, new Point(0, -316), 30, 36);
        Blob(dc, 0xFFC0C0C8, null, 0, new Point(-31, -324), new Point(-30, -300), new Point(-22, -306), new Point(-24, -326));
        Blob(dc, 0xFFC0C0C8, null, 0, new Point(31, -324), new Point(30, -300), new Point(22, -306), new Point(24, -326));
        dc.FillEllipse(-11, -320, 3.5, 0xFF201810);
        dc.FillEllipse(11, -320, 3.5, 0xFF201810);
        Blob(dc, 0xFFD0D0D8, 0xFF808088, 1.5, new Point(-22, -298), new Point(0, -306), new Point(22, -298), new Point(10, -292), new Point(0, -296), new Point(-10, -292));
        if (counting) dc.DrawEllipse(Brush(0xFF401010), null, new Point(0, -288), 7, 5);
        Poly(dc, 0xFF101010, null, 0, new Point(-16, -270), new Point(0, -264), new Point(16, -270), new Point(16, -256), new Point(0, -262), new Point(-16, -256));
    }
}
