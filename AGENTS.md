# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A boxing game in the style of Nintendo's 1984 arcade game Punch-Out!!, in C# on .NET 10 and Avalonia 12, drawn
with Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and
ARM). The boxers are drawn in code, and the sounds, voice and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAPunchOut.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAPunchOut.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAPunchOut          # play it; --no-audio for silence
PUNCH_SHOTS=$PWD/Images dotnet test tests/AVAPunchOut.Tests --filter ScreenshotTests   # refresh the README's pictures
PUNCH_PROBE=1 dotnet test tests/AVAPunchOut.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                              # how the computer boxer gets on (PUNCH_PROBE=mash: punches, no defence)
python3 tools/make_sounds.py                  # rebuild src/AVAPunchOut/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAPunchOut/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the bout's phases (the
    opponent's card, the rounds, knockdowns) on a fixed-step loop.
  - `Sim.cs` has the rules of the ring: punches, blocks, dodges, the power meter, knockdowns and the count.
  - `Circuit.cs` defines the six opponents (`Profile`): their cards, strength, guard, punches, tells and the order
    they throw them.
  - `World.cs` has the screen's measures (`Field`), `Dice` (the copyable random source), `Controls` and the bout's
    state.
  - `Pilot.cs` is the computer boxer, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAPunchOut/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, scaled to the
  window, and runs the 60 Hz timer. `GameCanvas.Boxers.cs` draws the opponent, your see-through outline and the
  referee.
- `src/AVAPunchOut/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAPunchOut.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `PilotProbe.cs` is a tuning report, not a
  check.
- `tools/`: `make_sounds.py` writes the sounds; `speech.py` is the small formant speech synthesiser it uses.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` turns frame time into fixed 1/60 s steps (`Field.Step`), and
  `Sim.Step` advances the bout by one. Each bout seeds its own `Dice`, so tests can box whole bouts
  deterministically. Draw random numbers from the world's `Dice` (`w.Dice`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Dy` is held: negative (up) makes a punch high, positive (down) blocks.
  Dodges and punches are presses, latched until `Read()` takes them; key auto-repeat must never count as a press.
  Test new controls through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData`, `AVAPunchOut`); tests load the table from a fresh temporary folder and run
  with `Silent = true`.
- **Line endings.** The sources are LF; `app.manifest` is CRLF. Keep each file's existing line endings so diffs
  show only real changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- Retuning an opponent in `Circuit.cs` changes how the computer boxer fares: run the probe.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
