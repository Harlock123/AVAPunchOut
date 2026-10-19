using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPunchOut.Engine;
using AVAPunchOut.Views;

namespace AVAPunchOut.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set PUNCH_SHOTS to a folder to save them
/// as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapunch-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PUNCH_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPunchOut-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>On to where the blinking words are lit.</summary>
    private static void Lit(GameCanvas c, double rate = 2)
    {
        double k = c.DrawClock * rate % 2;
        if (k >= 0.9) c.Advance((2.2 - k) / rate);
    }

    /// <summary>The computer boxer fights on until the moment comes.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 400)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            c.Advance(1 / 60.0);
        }
        return false;
    }

    private static void Moment(int bout, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(bout);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Lit(c);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 81450, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_opponents_card()
    {
        var (w, c, g) = Open();
        g.GoIntro(2);
        g.Run(90);
        c.Advance(1.5);
        Lit(c, 3);
        Assert.Equal(Phase.Intro, g.Phase);
        Shot(w, c, "intro");
    }

    /// <summary>His glove a moment from landing on you: a copy of the world, stepped on, says it will.</summary>
    private static bool AboutToLand(World w)
    {
        if (w.Him is not { S: Stance.Strike, Move: { Low: false, Special: false } } h || h.T < h.Dur * 0.7) return false;
        var ahead = w.Copy();
        for (int i = 0; i < 4; i++) Sim.Step(ahead, default);
        return ahead.You.Act == Act.Hurt && w.You.Act != Act.Hurt;
    }

    [AvaloniaFact]
    public void An_exchange() => Moment(5, "exchange", g => AboutToLand(g.World));

    [AvaloniaFact]
    public void A_tell_and_a_dodge() =>
        Moment(5, "dodge", g => g.World.Him is { S: Stance.Tell, Move.Special: true } && g.World.You is { Act: Act.Dodge, T: > 0.1 });

    [AvaloniaFact]
    public void A_counter_landing() =>
        Moment(5, "counter", g => g.World.Him.S == Stance.Open && g.World.Sparks.Any(s => !s.OnYou && !s.Blocked && s.Age is > 0.05 and < 0.12) && g.World.Call == "COUNTER!");

    [AvaloniaFact]
    public void A_knockdown_and_the_count() =>
        Moment(3, "knockdown", g => g.World.Downed == Who.Him && g.World.Count == 3 && g.World.CountT > 3.25);

    [AvaloniaFact]
    public void A_knockout() =>
        Moment(0, "knockout", g => g.Phase == Phase.Result && g.World.Outcome == Outcome.WonKo && g.PhaseTime > 1.6);
}
