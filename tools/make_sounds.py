#!/usr/bin/env python3
"""Synthesises AVAPunchOut's sound effects, voice and tunes into ../src/AVAPunchOut/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from sine, square, triangle and noise voices, with a few
simple filters. The title plays The Entertainer (Scott Joplin, 1902, in the public domain) on a honky-tonk piano; the
jingles are the game's own. The referee's count, "down", "fight" and "knock out" are spoken by the formant voice in
speech.py, through the arena's echo. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAPunchOut", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def at(seconds, part, total=None):
    """The part, starting <seconds> in."""
    out = silence(seconds) + part
    return out if total is None else (out + silence(total))[: int(RATE * total)]


def gain(part, g):
    return [s * g for s in part]


def lowpass(samples, cutoff):
    a = 1 - math.exp(-2 * math.pi * cutoff / RATE)
    out, y = [], 0.0
    for s in samples:
        y += a * (s - y)
        out.append(y)
    return out


def highpass(samples, cutoff):
    low = lowpass(samples, cutoff)
    return [s - l for s, l in zip(samples, low)]


def noise(seconds, attack=0.002, release=1.0, g=1.0):
    n = int(RATE * seconds)
    return [RNG.uniform(-1, 1) * min(1.0, i / RATE / attack) * (1 - i / n) ** release * g for i in range(n)]


def echo(samples, delays=((0.11, 0.35), (0.23, 0.18), (0.37, 0.08))):
    """The arena: the voice coming back off the far walls."""
    n = len(samples) + int(RATE * max(d for d, _ in delays)) + 1
    out = samples + [0.0] * (n - len(samples))
    for d, g in delays:
        k = int(RATE * d)
        for i, s in enumerate(samples):
            out[i + k] += s * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


# ───────────────────────────── instruments ─────────────────────────────

def piano(seconds, note, g=0.5):
    """A honky-tonk piano: two strings a shade apart, struck bright, dying away."""
    f = hz(note)
    body = mix(tone(seconds, f, kind="tri", attack=0.002, release=1.6, gain=g),
               tone(seconds, f * 1.004, kind="tri", attack=0.002, release=1.6, gain=g * 0.7),
               tone(min(seconds, 0.09), f * 2, kind="pulse", attack=0.001, release=1.0, gain=g * 0.22))
    return body


def brass(seconds, note, g=0.5, vibrato=0.006):
    f = hz(note)
    return lowpass(mix(tone(seconds, f, kind="saw", attack=0.02, release=0.35, vibrato=vibrato, vib_rate=5.5, gain=g),
                       tone(seconds, f * 0.5, kind="square", attack=0.02, release=0.35, gain=g * 0.25)), 3200)


def drum(seconds, g=1.0):
    """A bass drum: a falling thump."""
    return cat(tone(0.18, 120, 45, "sine", attack=0.001, release=1.0, gain=g), silence(max(0, seconds - 0.18)))


def snare(seconds, g=0.6):
    return cat(mix(highpass(noise(0.12, release=1.4), 1500), tone(0.06, 220, 160, "tri", attack=0.001, gain=0.5)), silence(max(0, seconds - 0.12)))


def play(bpm, notes, instrument, **kw):
    """(note, beats) pairs; '-' is a rest; a list of notes is a chord."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        elif isinstance(note, (list, tuple)):
            out.extend(mix(*[instrument(d, n, **kw) for n in note]))
        else:
            out.extend(instrument(d, note, **kw))
    return out


# ───────────────────────────── The Entertainer ─────────────────────────────

def entertainer():
    """The Entertainer (Scott Joplin, 1902): the famous first strain, in sixteenths, with an oom-pah left hand."""
    s = 0.25  # a sixteenth, in beats
    hook = [("D5", 1), ("D#5", 1), ("E5", 1), ("C6", 2), ("E5", 1), ("C6", 2), ("E5", 1), ("C6", 6)]
    answer = [("C6", 1), ("D6", 1), ("D#6", 1), ("E6", 1), ("C6", 1), ("D6", 1), ("E6", 2), ("B5", 1), ("D6", 2), ("C6", 5)]
    second = [("A5", 1), ("G5", 1), ("F#5", 1), ("A5", 1), ("C6", 1), ("E6", 2), ("D6", 1), ("C6", 1), ("A5", 1), ("D6", 7)]
    ending = [("C6", 1), ("D6", 1), ("E6", 1), ("C6", 1), ("D6", 1), ("E6", 2), ("C6", 1), ("D6", 1), ("C6", 1), ("E6", 1), ("C6", 1),
              ("D6", 1), ("E6", 2), ("C6", 1), ("D6", 1), ("C6", 1), ("E6", 1), ("C6", 1), ("D6", 1), ("E6", 2), ("B5", 1), ("D6", 2), ("C6", 6)]
    melody = hook + answer + hook + second + hook + ending
    tune = [(n, d * s) for n, d in melody]
    # The left hand: a bass note then a chord, on the eighths, by the bar (eight sixteenths).
    chords = {"C": ("C3", ["E4", "G4", "C5"]), "G": ("G2", ["F4", "G4", "B4"]), "F": ("F2", ["F4", "A4", "C5"]), "D": ("D3", ["D4", "F#4", "C5"])}
    bars = "CCGC" + "CCDG" + "CCCCGC"
    left = []
    for b in bars:
        root, chord = chords[b]
        left += [(root, 0.5), (chord, 0.5), (root[:-1] + str(int(root[-1]) + 1), 0.5), (chord, 0.5)]
    pickup = 2 * s
    bpm = 92
    right = play(bpm, tune, piano, g=0.55)
    bass = at(pickup * 60 / bpm, play(bpm, left, piano, g=0.28))
    return mix(right, bass)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # Punches through the air: yours, his (heavier), a dodge, and his glove whiffing past your ear.
    write("Swing.wav", highpass(tone(0.12, 2600, 900, "noise", attack=0.01, release=1.0), 600), 0.2)
    write("Whoosh.wav", lowpass(tone(0.16, 1800, 500, "noise", attack=0.02, release=0.8), 2200), 0.3)
    write("Dodge.wav", highpass(tone(0.14, 1400, 3400, "noise", attack=0.02, release=1.2), 900), 0.22)
    write("Whiff.wav", mix(lowpass(tone(0.26, 2400, 400, "noise", attack=0.01, release=0.9), 2600), tone(0.2, 300, 120, "sine", release=1.0, gain=0.3)), 0.32)
    # Landing: a thump and a slap to the face; a deeper thud to the body; glove on glove for a block.
    write("HitFace.wav", mix(tone(0.16, 160, 55, "sine", attack=0.001, release=1.0), highpass(noise(0.07, release=1.5, g=0.9), 1800),
                             tone(0.05, 900, 300, "square", attack=0.001, release=1.0, gain=0.25)), 0.5)
    write("HitBody.wav", mix(tone(0.2, 110, 40, "sine", attack=0.001, release=1.0, gain=1.2), lowpass(noise(0.09, release=1.5), 900)), 0.55)
    write("Block.wav", mix(lowpass(noise(0.06, release=1.6), 1400), tone(0.05, 260, 180, "sine", attack=0.001, gain=0.6)), 0.38)
    # The KO punch: a rising roar, then the crack.
    write("KoPunch.wav", cat(highpass(tone(0.22, 400, 2400, "noise", attack=0.05, release=0.3), 500),
                             mix(tone(0.3, 140, 40, "sine", attack=0.001, release=1.0, gain=1.3), highpass(noise(0.12, release=1.3), 1200),
                                 tone(0.12, 1200, 300, "square", attack=0.001, gain=0.25))), 0.6)
    # His tell for a big one: a bright warning ting; the meter full: a rising chime.
    write("Tell.wav", mix(tone(0.35, hz("E6"), kind="sine", attack=0.002, release=1.4), tone(0.35, hz("E6") * 2.01, kind="sine", attack=0.002, release=2.0, gain=0.4)), 0.32)
    write("Star.wav", cat(*[tone(0.06, hz(n), kind="pulse", attack=0.002, release=0.8, gain=0.6) for n in ("C6", "E6", "G6")], tone(0.25, hz("C7"), kind="tri", release=1.0)), 0.3)
    # Down on the canvas: a heavy thud and the ropes rattling.
    write("Fall.wav", mix(tone(0.45, 90, 32, "sine", attack=0.002, release=1.0, gain=1.4), lowpass(noise(0.3, release=1.2), 600),
                          at(0.05, gain(highpass(noise(0.4, attack=0.02, release=1.5), 2500), 0.15))), 0.62)
    # The bell: ding-ding-ding.
    def ding(g=1.0):
        f = 1046
        return mix(*[tone(1.1, f * r, kind="sine", attack=0.001, release=3 + i, gain=g * a) for i, (r, a) in enumerate(((1, 1), (2.76, 0.5), (5.4, 0.25), (8.93, 0.12)))])
    write("Bell.wav", mix(ding(), at(0.18, ding(0.9)), at(0.36, ding(0.85))), 0.5)
    # The crowd: a murmur that loops, and a roar for a knockdown.
    n = int(RATE * 6)
    murmur = lowpass(lowpass([RNG.uniform(-1, 1) for _ in range(n)], 700), 900)
    swell = [1 + 0.35 * math.sin(2 * math.pi * i / n * 3) + 0.2 * math.sin(2 * math.pi * i / n * 7 + 1) for i in range(n)]
    crowd = [m * s for m, s in zip(murmur, swell)]
    # Ends faded into each other so the loop doesn't click.
    fade = int(RATE * 0.5)
    for i in range(fade):
        k = i / fade
        crowd[i] = crowd[i] * k + crowd[n - fade + i] * (1 - k)
    write("Crowd.wav", crowd[: n - fade], 0.35)
    roar = lowpass([RNG.uniform(-1, 1) for _ in range(int(RATE * 1.8))], 1600)
    roar = [s * min(1, i / (RATE * 0.2)) * (1 - i / len(roar)) ** 1.2 for i, s in enumerate(roar)]
    whoops = mix(*[at(0.1 + k * 0.17, tone(0.4, 500 + k * 90, 900 + k * 80, "sine", attack=0.05, release=1.0, vibrato=0.03, vib_rate=7, gain=0.12)) for k in range(5)])
    write("Cheer.wav", mix(roar, whoops), 0.5)

    # The referee: the count, "down", "fight", "knock out", through the arena's echo.
    words = {
        "SayOne": "W AH1 N", "SayTwo": "T UW1", "SayThree": "TH R IY1", "SayFour": "F AO1 R", "SayFive": "F AA1 IY V",
        "SaySix": "S IH1 K S", "SaySeven": "S EH1 V AH N", "SayEight": "EY1 T", "SayNine": "N AA1 IY N", "SayTen": "T EH1 N !",
        "SayDown": "D AA1 UW N !", "SayFight": "F AA1 IY T !", "SayKnockOut": "N AA1 K , AA1 UW T !",
    }
    for name, text in words.items():
        write(name + ".wav", echo(say(text, rate=RATE, pitch=112, speed=0.85, size=1.05)), 0.6)

    # The jingles: the fanfare for the card, the knockout, a win on points, a loss, the end.
    q = 1
    write("Fanfare.wav", mix(play(150, [("G4", 1 / 3), ("G4", 1 / 3), ("G4", 1 / 3), ("C5", 1), ("E5", 1 / 2), ("G5", 1 / 2), ("E5", 1 / 2), ("G5", 2)], brass, g=0.5),
                             play(150, [("C3", 1), ("G2", 1), ("C3", 2), ("G2", 1), ("C3", 1)], brass, g=0.3)), 0.45)
    ko = [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("-", 0.25), ("A5", 0.25), ("B5", 0.25), ("C6", 0.25), ("D6", 0.5), ("E6", 0.5), ("C6", 2)]
    write("KoJingle.wav", mix(play(140, ko, brass, g=0.5), play(140, [(["C4", "E4", "G4"], 2), (["F4", "A4", "C5"], 1), (["G4", "B4", "D5"], 1), (["C4", "E4", "G4"], 2)], piano, g=0.3),
                              cat(drum(0.43), drum(0.43), snare(0.43), drum(0.43), snare(0.21), snare(0.21), drum(0.86))), 0.45)
    write("Win.wav", mix(play(132, [("E5", 0.5), ("G5", 0.5), ("C6", 1), ("B5", 0.5), ("D6", 0.5), ("C6", 2)], brass, g=0.5),
                         play(132, [(["C4", "E4", "G4"], 2), (["G3", "B3", "D4"], 1), (["C4", "E4", "G4"], 2)], piano, g=0.3)), 0.42)
    write("Lose.wav", play(84, [("G4", 0.75), ("F#4", 0.75), ("F4", 0.75), ("E4", 3)], brass, g=0.5, vibrato=0.02), 0.42)
    write("GameOver.wav", mix(play(66, [("A4", q), ("E4", q), ("C4", q), ("D4", 0.5), ("C4", 0.5), ("A3", 3)], brass, g=0.45, vibrato=0.012),
                              play(66, [(["A2", "E3"], 2), (["F2", "C3"], 2), (["E2", "B2"], 1), (["A2", "E3"], 2)], piano, g=0.3)), 0.42)
    # The title: The Entertainer.
    write("TitleTune.wav", cat(entertainer(), silence(1.5)), 0.42)


if __name__ == "__main__":
    main()
