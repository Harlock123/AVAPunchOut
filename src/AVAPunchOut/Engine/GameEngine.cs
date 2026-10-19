namespace AVAPunchOut.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The next opponent's card: his record, weight, home town and a taunt.</summary>
    Intro,
    /// <summary>The bell, and the round called.</summary>
    Bell,
    Fight,
    /// <summary>Between rounds, on the stools.</summary>
    Break,
    /// <summary>The fight over: the KO, the T.K.O. or the decision, and the bonuses.</summary>
    Result,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Swing, Whoosh, HitFace, HitBody, Block, Dodge, Whiff, Tell, KoPunch, Star, Fall, Bell, Cheer,
    SayOne, SayTwo, SayThree, SayFour, SayFive, SaySix, SaySeven, SayEight, SayNine, SayTen,
    SayDown, SayKnockOut, SayFight,
    Crowd, CrowdStop, Fanfare, KoJingle, Win, Lose, TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double IntroSeconds = 6, BellSeconds = 2.0, BreakSeconds = 3.5, ResultSeconds = 5.5, OverSeconds = 5;
    /// <summary>Lose this many fights and you're off the circuit.</summary>
    public const int Lives0 = 2;
    public const int KoBonus = 2000, TkoBonus = 1500, DecisionBonus = 800, TimeBonusPerSecond = 5;

    private double _accumulator, _idle;
    private Controls _pending;
    private bool _startPrev, _tunePending = true, _crowd;
    private int _attracts;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame(0);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Intro;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    /// <summary>Where on the circuit: the bout (from 0) and how many times round.</summary>
    public int Bout { get; private set; }
    public int Lap { get; private set; } = 1;
    public Profile Opponent => World.P;
    /// <summary>Your record: fights won and lost (shown on the cards, and the wins with the high score).</summary>
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    /// <summary>The bonuses for the fight just won.</summary>
    public int FinishBonus { get; private set; }
    public int TimeBonus { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    /// <summary>Presses are taken once each: kept until a step takes them, even if no step runs this frame.</summary>
    public void Update(double dt, Controls c, bool start)
    {
        _pending = new Controls(_pending.Dx != 0 ? _pending.Dx : c.Dx, c.Dy, _pending.Left || c.Left, _pending.Right || c.Right, _pending.Ko || c.Ko);
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(_pending, start);
            _pending = new Controls(Dy: c.Dy);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || (c.Left || c.Right) && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this), false);
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Crowd(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c, startPressed);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Wins);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame(int bout)
    {
        Bout = bout;
        Lap = 1;
        Lives = Lives0;
        Wins = Losses = 0;
        World = new World();
        Sim.Start(World, Bout, Lap);
        Pilot.Reset();
        SetPhase(Phase.Intro);
    }

    public void StartGame()
    {
        NewGame(0);
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        BeginBout();
    }

    /// <summary>The demonstration: the computer boxer, from a different opponent each time, on round the circuit until the time's up.</summary>
    private void StartAttract()
    {
        NewGame(_attracts++ % Circuit.Count);
        State = GameState.Attract;
        StateTime = 0;
        BeginBout();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Crowd(false);
        NewGame(0);
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Crowd(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        Crowd(true);
    }

    private void Crowd(bool on)
    {
        if (on == _crowd) return;
        _crowd = on;
        Play(on ? Sfx.Crowd : Sfx.CrowdStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    /// <summary>A new fight against the opponent at <see cref="Bout"/>, starting with his card.</summary>
    private void BeginBout()
    {
        int score = World.Score;
        World = new World();
        Sim.Start(World, Bout, Lap, score);
        Pilot.Reset();
        SetPhase(Phase.Intro);
        Crowd(true);
        Play(Sfx.Fanfare);
    }

    private void Simulate(double dt, Controls c, bool startPressed)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Intro:
                // The card can be skipped once it's been seen a moment.
                bool skip = State == GameState.Playing && PhaseTime > 1.0 && (startPressed || c.Left || c.Right);
                if (skip || PhaseTime > (State == GameState.Attract ? IntroSeconds * 0.6 : IntroSeconds)) Bell();
                break;
            case Phase.Bell:
                if (PhaseTime > BellSeconds) SetPhase(Phase.Fight);
                break;
            case Phase.Fight:
                Sim.Step(w, c);
                if (w.Outcome != Outcome.None) Result();
                else if (w.RoundOver) SetPhase(Phase.Break);
                break;
            case Phase.Break:
                if (PhaseTime > BreakSeconds)
                {
                    Sim.NextRound(w);
                    Bell();
                }
                break;
            case Phase.Result:
                Sim.Step(w, default);
                if (PhaseTime > ResultSeconds) AfterResult();
                break;
        }
        if (World != w) return;
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void Bell()
    {
        SetPhase(Phase.Bell);
        Play(Sfx.Bell);
        Play(Sfx.SayFight);
    }

    /// <summary>The fight decided: bonuses for a win (more for a knockout, and for the time left on the clock), the jingle.</summary>
    private void Result()
    {
        var w = World;
        SetPhase(Phase.Result);
        FinishBonus = TimeBonus = 0;
        if (w.Outcome.Won())
        {
            FinishBonus = (w.Outcome switch { Outcome.WonKo => KoBonus, Outcome.WonTko => TkoBonus, _ => DecisionBonus }) * (Bout + 1) * Lap;
            double left = (Sim.Rounds - w.Round) * Sim.RoundSeconds + Sim.RoundSeconds - w.Clock;
            TimeBonus = w.Outcome == Outcome.WonDecision ? 0 : (int)left * TimeBonusPerSecond;
            w.Score += FinishBonus + TimeBonus;
            Play(w.Outcome == Outcome.WonDecision ? Sfx.Win : Sfx.KoJingle);
        }
        else Play(Sfx.Lose);
    }

    /// <summary>After the result: on to the next opponent (round the circuit again after the champion), a rematch, or the end.</summary>
    private void AfterResult()
    {
        if (World.Outcome.Won())
        {
            Wins++;
            if (++Bout >= Circuit.Count)
            {
                Bout = 0;
                Lap++;
            }
            BeginBout();
            return;
        }
        Losses++;
        Lives--;
        if (Lives <= 0 || State == GameState.Attract)
        {
            End();
            return;
        }
        BeginBout();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Crowd(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into the fight against the <paramref name="bout"/>th opponent, past his card and the bell.</summary>
    public void Go(int bout = 0, int lap = 1)
    {
        NewGame(bout);
        Lap = lap;
        State = GameState.Playing;
        StateTime = 0;
        BeginBout();
        SetPhase(Phase.Fight);
    }

    /// <summary>Into the opponent's card before the fight.</summary>
    public void GoIntro(int bout = 0, int lap = 1)
    {
        Go(bout, lap);
        SetPhase(Phase.Intro);
    }

    public void SetLives(int n) => Lives = n;

    /// <summary>Plays on with the computer boxer until the condition holds (or the time runs out).</summary>
    public bool PilotUntil(Func<GameEngine, bool> done, int maxSeconds = 600)
    {
        for (int t = 0; t < 60 * maxSeconds && State == GameState.Playing; t++)
        {
            if (done(this)) return true;
            Tick(Pilot.Decide(this));
        }
        return done(this);
    }
}
