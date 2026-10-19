using AVAPunchOut.Engine;

namespace AVAPunchOut.Tests;

/// <summary>How the computer boxer gets on round the circuit (set PUNCH_PROBE to run it; PUNCH_PROBE=mash punches
/// away without defending).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_boxer_does()
    {
        if (Environment.GetEnvironmentVariable("PUNCH_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapunch-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go();
        g.SetLives(mode == "mash" ? 1 : 3);
        int t = 0;
        var phase = g.Phase;
        for (; t < 60 * 60 * 60 && g.State == GameState.Playing; t++)
        {
            var c = mode == "mash" ? new Controls(Left: t % 20 == 0, Right: t % 20 == 10, Dy: t % 40 < 20 ? -1 : 0) : g.Pilot.Decide(g);
            g.Tick(c);
            if (g.Phase == Phase.Result && phase != Phase.Result)
            {
                var w = g.World;
                output.WriteLine($"{w.Bout + 1}/{w.Lap} {w.P.Name,-20} {w.Outcome,-13} round {w.Round} at {Clock(w.Clock)}; downs {w.Downs[0]}-{w.Downs[1]}, " +
                                 $"stamina {w.You.Hp:0}, landed {w.Landed}/{w.Thrown}, counters {w.Counters}, points {w.Points[0]}-{w.Points[1]}, score {g.Score}");
            }
            phase = g.Phase;
        }
        output.WriteLine($"{g.State} after {t / 60}s: won {g.Wins}, lost {g.Losses}, score {g.Score}; {sw.ElapsedMilliseconds} ms");
    }

    private static string Clock(double s) => $"{(int)s / 60}:{(int)s % 60:00}";
}
