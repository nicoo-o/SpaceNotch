"""Soundtrack for the SpaceNotch teaser: one piece, music and effects in the same key (D minor → D major at the end)."""
import wave
import numpy as np

SR = 44100
DUR = 22.5
N = int(SR * DUR)
t_all = np.arange(N) / SR
rng = np.random.default_rng(3)


def note(name):
    names = {'C': -9, 'C#': -8, 'D': -7, 'D#': -6, 'E': -5, 'F': -4, 'F#': -3, 'G': -2, 'G#': -1, 'A': 0, 'A#': 1, 'Bb': 1, 'B': 2}
    pitch, octave = name[:-1], int(name[-1])
    return 440.0 * 2 ** ((names[pitch] + (octave - 4) * 12) / 12)


def env(n, a, d, s=0.0, r=0.0, sus_len=0.0):
    """ADSR in seconds, returned for n samples."""
    x = np.arange(n) / SR
    out = np.zeros(n)
    out = np.where(x < a, x / max(a, 1e-4), out)
    dec = (x >= a) & (x < a + d)
    out = np.where(dec, 1 - (1 - s) * (x - a) / max(d, 1e-4), out)
    sus = (x >= a + d) & (x < a + d + sus_len)
    out = np.where(sus, s, out)
    rel = x >= a + d + sus_len
    out = np.where(rel, s * np.exp(-(x - a - d - sus_len) / max(r, 1e-4)), out)
    return out


def onepole(x, cutoff):
    """Low-pass; cutoff may be an array (sweeps)."""
    cutoff = np.broadcast_to(cutoff, x.shape)
    a = 1 - np.exp(-2 * np.pi * cutoff / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def place(buf, start, sig, gain=1.0):
    i = int(start * SR)
    if i >= len(buf):
        return
    j = min(len(buf), i + len(sig))
    buf[i:j] += sig[: j - i] * gain


def saw(freq, n, detune=0.0):
    ph = np.cumsum(np.full(n, freq * (1 + detune)) / SR)
    return 2 * (ph % 1) - 1


def sine(freq, n):
    return np.sin(2 * np.pi * np.cumsum(np.broadcast_to(freq, (n,))) / SR)


music = np.zeros(N)
sfx = np.zeros(N)

# ---------------- pad: warm detuned saws, slowly opening ----------------
chords = [
    (0.0, 3.2, ['D3', 'A3', 'F4']),          # hook: Dm, just a breath
    (3.2, 7.6, ['Bb2', 'F3', 'D4', 'A4']),   # desktop appears: Bbmaj7
    (7.6, 10.6, ['F2', 'C4', 'F4', 'A4']),   # F
    (10.6, 13.4, ['C3', 'G3', 'E4', 'G4']),  # C
    (13.4, 16.8, ['D3', 'A3', 'F4', 'C5']),  # Dm7
    (16.8, 19.35, ['G2', 'D4', 'G4', 'Bb4']),  # Gm
    (19.35, 22.5, ['D2', 'A3', 'F#4', 'A4', 'D5']),  # D major: resolution
]
for (a, b, notes) in chords:
    n = int((b - a + 1.2) * SR)
    ch = np.zeros(n)
    for nm in notes:
        f = note(nm)
        ch += saw(f, n, -0.004) + saw(f, n, 0.0045) + 0.5 * sine(f * 0.5, n)
    ch /= len(notes) * 2.5
    e = env(n, 0.9 if a == 0 else 0.35, 0.3, 0.85, 0.9, sus_len=b - a - 0.4)
    place(music, a, ch * e, 0.55)
open_curve = 500 + 2600 * np.clip((t_all - 3.0) / 6, 0, 1) - 900 * np.clip((t_all - 19.3) / 2.5, 0, 1)
music = onepole(music, open_curve)

# ---------------- beat: soft kick + airy hats, 96 bpm, from the reveal ----------------
beat = 60 / 96
kick_n = int(0.45 * SR)
kx = np.arange(kick_n) / SR
kick = np.sin(2 * np.pi * (48 * kx + 90 * (1 - np.exp(-kx * 30)) / 30)) * np.exp(-kx * 9)
hat = onepole(rng.standard_normal(int(0.08 * SR)), 9000)
hat = (rng.standard_normal(int(0.08 * SR)) - hat) * np.exp(-np.arange(int(0.08 * SR)) / SR * 55)
t = 3.35
i = 0
while t < 19.2:
    if i % 2 == 0:
        place(music, t, kick, 0.55)
    place(music, t + beat / 2, hat, 0.05)
    t += beat
    i += 1
# Bass pulse on the kicks, following the chords.
roots = {3.2: 'Bb1', 7.6: 'F1', 10.6: 'C2', 13.4: 'D2', 16.8: 'G1'}
for start, nm in roots.items():
    end = min([s for s in roots if s > start] + [19.35])
    tt = start + ((3.35 - start) % (beat * 2) if start < 3.35 else 0)
    while tt < end:
        n = int(beat * 1.6 * SR)
        place(music, tt, sine(note(nm), n) * env(n, 0.01, 0.2, 0.5, 0.25, beat * .8), 0.32)
        tt += beat * 2

# ---------------- effects, all in key ----------------
def bell(freqs, dur=1.6, bright=1.0):
    n = int(dur * SR)
    x = np.arange(n) / SR
    s = np.zeros(n)
    for f in freqs:
        s += np.sin(2 * np.pi * f * x) * np.exp(-x * 3.5) + 0.3 * bright * np.sin(2 * np.pi * f * 2.01 * x) * np.exp(-x * 7)
    return s / len(freqs) * env(n, 0.004, 0.05, 1.0, 1.0, dur)


def whoosh(dur, up=True, lo=300, hi=4500):
    n = int(dur * SR)
    x = np.linspace(0, 1, n)
    curve = lo + (hi - lo) * (x if up else 1 - x) ** 1.5
    s = onepole(rng.standard_normal(n), curve)
    return s * np.sin(np.pi * x) ** 1.4


def thump(f0=90, f1=38, dur=0.7):
    n = int(dur * SR)
    x = np.arange(n) / SR
    f = f1 + (f0 - f1) * np.exp(-x * 12)
    return sine(f, n) * np.exp(-x * 5)


def blip(f0, f1, dur=0.18):
    n = int(dur * SR)
    x = np.linspace(0, 1, n)
    return sine(f0 + (f1 - f0) * x, n) * np.exp(-x * 6) * np.sin(np.pi * np.minimum(x * 8, 1) / 2)


def click(dur=0.03, cut=5000):
    n = int(dur * SR)
    return onepole(rng.standard_normal(n), cut) * np.exp(-np.arange(n) / SR * 180)


place(sfx, 0.15, bell([note('D6')], 2.2, 0.4), 0.35)                        # the first pixel
for k, nm in enumerate(['D5', 'F5', 'A5', 'D6']):                            # the grid blooms
    place(sfx, 1.05 + k * 0.07, bell([note(nm)], 1.2, 0.6), 0.16)
place(sfx, 1.25, thump(), 0.9)                                                # the notch drops
place(sfx, 2.9, whoosh(0.9, True, 200, 3000), 0.25)                           # desktop reveal
place(sfx, 3.2, thump(70, 36, 1.2), 0.6)
place(sfx, 4.25, whoosh(0.45, True, 400, 5000), 0.2)                          # grows into the player
place(sfx, 7.5, whoosh(0.4, False, 300, 3500), 0.14)
for k in range(15):                                                           # work ticks, background
    place(sfx, 8.1 + k * 0.111, blip(note('A5'), note('A5') * 0.99, 0.05), 0.03)
place(sfx, 9.7, bell([note('A5'), note('D6'), note('F#6')], 2.0), 0.26)      # done
place(sfx, 11.12, blip(note('D5'), note('A5'), 0.22), 0.22)                  # bubble splits
place(sfx, 11.35, blip(note('A5'), note('D6'), 0.12), 0.12)
place(sfx, 13.85, click(), 0.35)                                              # Alt+Space
place(sfx, 13.92, click(), 0.3)
place(sfx, 14.05, whoosh(0.4, True, 500, 5500), 0.16)
for k in range(4):                                                            # typing
    place(sfx, 14.55 + k * 0.13, click(0.025, 7000), 0.18)
for k in range(4):                                                            # results
    place(sfx, 15.1 + k * 0.1, blip(note('D6'), note('D6'), 0.06), 0.025)
n = int(0.75 * SR)                                                            # the stretch
place(sfx, 17.0, sine(np.linspace(note('D3'), note('A3'), n), n) * np.linspace(0, 1, n) ** 2 * 0.5, 0.35)
place(sfx, 17.75, blip(note('A4'), note('D6'), 0.16), 0.3)                    # snaps free
place(sfx, 18.9, whoosh(0.5, False, 300, 3000), 0.14)
place(sfx, 19.3, thump(80, 34, 1.6), 0.85)                                    # the outro
place(sfx, 19.35, bell([note('D5'), note('F#5'), note('A5'), note('D6')], 3.0, 0.5), 0.2)

# ---------------- space: one shared room so everything blends ----------------
def reverb(x, secs=2.2, wet=0.28):
    n = int(secs * SR)
    ir = rng.standard_normal(n) * np.exp(-np.arange(n) / SR * 3.0)
    ir = onepole(ir, 5000)
    ir /= np.sqrt(np.sum(ir ** 2))
    size = 1 << int(np.ceil(np.log2(len(x) + n)))
    y = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[: len(x)]
    return x * (1 - wet) + y * wet * 1.6

mix = reverb(music, 2.4, 0.22) * 0.9 + reverb(sfx, 1.8, 0.35) * 0.75
# Gentle glue: soft saturation, then fades at both ends (the loop starts and ends in black).
mix = np.tanh(mix * 1.4) / 1.4
fade = np.minimum(np.clip(t_all / 0.3, 0, 1), np.clip((DUR - t_all) / 1.2, 0, 1))
mix *= fade
mix /= np.max(np.abs(mix)) / 0.89

stereo = np.stack([mix, mix], axis=1)
# A touch of width: delay the right channel by 9 ms on the reverb-heavy content.
d = int(0.009 * SR)
stereo[d:, 1] = 0.85 * mix[d:] + 0.15 * mix[:-d]
pcm = (np.clip(stereo, -1, 1) * 32767).astype(np.int16)
with wave.open(__file__.replace('music.py', 'music.wav'), 'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())
print('ok', pcm.shape)
