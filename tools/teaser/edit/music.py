"""Soundtrack for the SpaceNotch teaser: one piece, music and effects in the same key (D minor → D major at the end)."""
import wave
import numpy as np

SR = 44100
import json
TL = json.load(open(__file__.replace('music.py', 'timeline.json')))
DUR = TL['total']
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

cuts = [s['t0'] for s in TL['segments']]
foot_end = cuts[-1] + TL['segments'][-1]['dur']
prog_ = [['Bb2', 'F3', 'D4', 'A4'], ['F2', 'C4', 'F4', 'A4'], ['C3', 'G3', 'E4', 'G4'], ['D3', 'A3', 'F4', 'C5'], ['G2', 'D4', 'G4', 'Bb4']]
chords = [(0.0, cuts[0], ['D3', 'A3', 'F4'])]
bounds = cuts[::2] + [foot_end]                       # a chord every two shots
for k in range(len(bounds) - 1):
    chords.append((bounds[k], bounds[k + 1], prog_[k % len(prog_)]))
chords.append((foot_end, DUR, ['D2', 'A3', 'F#4', 'A4', 'D5']))

for (a, b, notes) in chords:
    n = int((b - a + 1.2) * SR)
    ch = np.zeros(n)
    for nm in notes:
        f = note(nm)
        ch += saw(f, n, -0.004) + saw(f, n, 0.0045) + 0.5 * sine(f * 0.5, n)
    ch /= len(notes) * 2.5
    e = env(n, 0.9 if a == 0 else 0.35, 0.3, 0.85, 0.9, sus_len=max(0.1, b - a - 0.4))
    place(music, a, ch * e, 0.55)
open_curve = 500 + 2600 * np.clip((t_all - cuts[0]) / 6, 0, 1) - 900 * np.clip((t_all - foot_end) / 2.5, 0, 1)
music = onepole(music, open_curve)

beat = 60 / 96
kick_n = int(0.45 * SR)
kx = np.arange(kick_n) / SR
kick = np.sin(2 * np.pi * (48 * kx + 90 * (1 - np.exp(-kx * 30)) / 30)) * np.exp(-kx * 9)
hn = int(0.08 * SR)
hat = rng.standard_normal(hn)
hat = (hat - onepole(hat, 9000)) * np.exp(-np.arange(hn) / SR * 55)
t = cuts[0]; i = 0
while t < foot_end - 0.2:
    if i % 2 == 0:
        place(music, t, kick, 0.55)
        place(music, t, sine(note('D2'), int(beat * 1.6 * SR)) * env(int(beat * 1.6 * SR), 0.01, 0.2, 0.5, 0.25, beat * .8), 0.28)
    place(music, t + beat / 2, hat, 0.05)
    t += beat; i += 1

def bell(freqs, dur=1.6, bright=1.0):
    n = int(dur * SR); x = np.arange(n) / SR; s = np.zeros(n)
    for f in freqs:
        s += np.sin(2 * np.pi * f * x) * np.exp(-x * 3.5) + 0.3 * bright * np.sin(2 * np.pi * f * 2.01 * x) * np.exp(-x * 7)
    return s / len(freqs) * env(n, 0.004, 0.05, 1.0, 1.0, dur)

def whoosh(dur, up=True, lo=300, hi=4500):
    n = int(dur * SR); x = np.linspace(0, 1, n)
    curve = lo + (hi - lo) * (x if up else 1 - x) ** 1.5
    return onepole(rng.standard_normal(n), curve) * np.sin(np.pi * x) ** 1.4

def thump(f0=90, f1=38, dur=0.7):
    n = int(dur * SR); x = np.arange(n) / SR
    return sine(f1 + (f0 - f1) * np.exp(-x * 12), n) * np.exp(-x * 5)

def blip(f0, f1, dur=0.18):
    n = int(dur * SR); x = np.linspace(0, 1, n)
    return sine(f0 + (f1 - f0) * x, n) * np.exp(-x * 6) * np.sin(np.pi * np.minimum(x * 8, 1) / 2)

place(sfx, 0.3, bell([note('D6')], 2.2, 0.4), 0.3)                      # the title
place(sfx, cuts[0] - 0.5, whoosh(0.7, True, 200, 3000), 0.22)          # the desk appears
place(sfx, cuts[0], thump(70, 36, 1.2), 0.6)
place(sfx, cuts[0] + 1.0, bell([note('A5'), note('D6')], 1.4, 0.5), 0.14)  # the notch wakes
notes_ = ['A5', 'D6', 'F6', 'A5', 'C6', 'E6', 'D6']
for k, c in enumerate(cuts[1:]):                                        # each new state: a soft blip in key
    place(sfx, c, blip(note(notes_[k % len(notes_)]) * 0.5, note(notes_[k % len(notes_)]), 0.14), 0.12)
    place(sfx, c - 0.2, whoosh(0.35, True, 400, 4000), 0.06)
place(sfx, foot_end, thump(80, 34, 1.6), 0.8)                          # outro
place(sfx, foot_end + 0.05, bell([note('D5'), note('F#5'), note('A5'), note('D6')], 3.0, 0.5), 0.2)

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
