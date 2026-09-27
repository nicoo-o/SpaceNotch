"""The edit: which piece of real footage goes where, and the captions over it."""
import json, os, subprocess, sys

SRC = sys.argv[1]
ONLY = [a for a in sys.argv[2:] if a != 'extract']
FPS = 30
XF = 0.3   # crossfade

# (name, source start, output duration, speed, crop width, caption, sub-caption)
# The crop is centred on the notch, top edge of the screen: 960 px = ×2, 1280 px = ×1.5.
SEGMENTS = [
    ('appear',   6.4, 3.4, 1.0,  960, 'SpaceNotch', 'A small piece of darkness at the top of your screen.'),
    ('volume',  10.0, 2.0, 1.0,  960, 'Volume, as a quiet overlay.', None),
    ('discord', 14.0, 2.0, 1.0,  960, 'Notifications, grouped by app.', None),
    ('download',21.0, 2.4, 2.9,  960, 'Downloads from any browser.', None),
    ('airpods', 29.0, 1.4, 1.0,  960, 'Bluetooth, with battery.', None),
    ('grid',    41.2, 2.0, 1.0,  960, 'Alive, never busy.', None),
    ('search',  55.0, 2.8, 1.0, 1280, 'Alt + Space: apps, files, quick maths.', None),
]
INTRO = 2.4
OUTRO = 3.0

def extract():
    os.makedirs('src', exist_ok=True)
    for name, start, dur, speed, cw, *_ in SEGMENTS:
        if ONLY and name not in ONLY:
            continue
        out = f'src/{name}'
        os.makedirs(out, exist_ok=True)
        need = (dur + XF) * speed
        ch = cw * 9 // 16
        vf = f'setpts=PTS/{speed},fps={FPS},crop={cw}:{ch}:{(1920 - cw) // 2}:0,scale=1920:1080:flags=lanczos'
        subprocess.run(['ffmpeg', '-loglevel', 'error', '-y', '-ss', str(start), '-t', str(need), '-i', SRC,
                        '-vf', vf, '-q:v', '2', f'{out}/%04d.jpg'], check=True)
        print(name, len(os.listdir(out)))

def timeline():
    t = INTRO
    segs = []
    for name, start, dur, speed, cw, cap, sub in SEGMENTS:
        if not os.path.isdir(f'src/{name}'):
            continue
        n = len(os.listdir(f'src/{name}'))
        segs.append(dict(name=name, t0=t, dur=dur, frames=n, cap=cap, sub=sub))
        t += dur
    total = t + OUTRO
    json.dump(dict(segments=segs, intro=INTRO, outro=OUTRO, total=total, xf=XF, fps=FPS), open('timeline.json', 'w'), indent=1)
    print('total', round(total, 2))

if __name__ == '__main__':
    if 'extract' in sys.argv:
        extract()
    timeline()
