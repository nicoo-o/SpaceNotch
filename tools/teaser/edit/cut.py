"""The edit: which piece of real footage goes where, and the captions over it.

    python3 cut.py <footage dir> extract     # frames for every shot + timeline.json
    python3 cut.py <footage dir>             # timeline.json only

Shots of the demo are timed from the moment the demo starts, read from the logs
(`[DEMO] scénario lancé` in spacenotch.log, recording start in clock.json), so a new
take needs no re-timing. Shots of scripted input are timed on the recording clock,
which is film.py's own. `--demo-start 7.2` overrides the detection.
"""
import datetime, json, os, re, subprocess, sys

args = [a for a in sys.argv[1:] if not a.startswith('--')]
FOOTAGE = args[0]
SRC = os.path.join(FOOTAGE, 'footage.mp4')
EXTRACT = 'extract' in args
ONLY = [a for a in args[1:] if a != 'extract']
FPS = 30
XF = 0.3   # crossfade

# (name, clock, start, output duration, speed, crop width, caption, sub-caption)
# clock 'demo': seconds after the demo starts (DemoScenario: music 0, volume 3, Discord 7,
# download 13, AirPods 22, working grid 26–46). clock 'rec': seconds into the recording
# (film.py: Alt+Space at 53, typing at 55.5).
# The crop is centred on the notch, top edge of the screen: 960 px = ×2, 1280 px = ×1.5.
SEGMENTS = [
    ('appear',   'demo', -0.8, 3.4, 1.0,  960, 'SpaceNotch', 'A small piece of darkness at the top of your screen.'),
    ('volume',   'demo',  2.8, 2.0, 1.0,  960, 'Volume, as a quiet overlay.', None),
    ('discord',  'demo',  6.8, 2.0, 1.0,  960, 'Notifications, grouped by app.', None),
    ('download', 'demo', 13.8, 2.4, 2.9,  960, 'Downloads from any browser.', None),
    ('airpods',  'demo', 21.8, 1.4, 1.0,  960, 'Bluetooth, with battery.', None),
    ('grid',     'demo', 34.0, 2.0, 1.0,  960, 'Alive, never busy.', None),
    ('search',   'rec',  55.0, 2.8, 1.0, 1280, 'Alt + Space: apps, files, quick maths.', None),
]
INTRO = 2.4
OUTRO = 3.0


def demo_start():
    for a in sys.argv:
        if a.startswith('--demo-start='):
            return float(a.split('=', 1)[1])
    clock = json.load(open(os.path.join(FOOTAGE, 'clock.json')))
    # Les runners Windows tournent en UTC : le journal de l'app est en heure UTC.
    start = datetime.datetime.fromtimestamp(clock['recording_start_unix'], datetime.timezone.utc).replace(tzinfo=None)
    for line in open(os.path.join(FOOTAGE, 'spacenotch.log'), encoding='utf-8', errors='replace'):
        m = re.match(r'\[(\d\d):(\d\d):(\d\d)\.(\d+)\] \[DEMO\] scénario lancé', line)
        if m:
            h, mi, se, ms = (int(x) for x in m.groups())
            demo = start.replace(hour=h, minute=mi, second=se, microsecond=int(str(ms).ljust(6, '0')[:6]))
            return (demo - start).total_seconds()
    raise SystemExit('Début de la démo introuvable dans spacenotch.log')


def extract(d0):
    os.makedirs('src', exist_ok=True)
    for name, clock, at, dur, speed, cw, *_ in SEGMENTS:
        if ONLY and name not in ONLY:
            continue
        start = at + (d0 if clock == 'demo' else 0)
        if start < 0:
            raise SystemExit(f'{name} : commencerait avant l\'enregistrement ({start:.2f} s) — horloge ou journal incohérents')
        out = f'src/{name}'
        subprocess.run(['rm', '-rf', out])
        os.makedirs(out)
        need = (dur + XF) * speed
        ch = cw * 9 // 16
        vf = f'setpts=PTS/{speed},fps={FPS},crop={cw}:{ch}:{(1920 - cw) // 2}:0,scale=1920:1080:flags=lanczos'
        subprocess.run(['ffmpeg', '-loglevel', 'error', '-y', '-ss', f'{start:.2f}', '-t', str(need), '-i', SRC,
                        '-vf', vf, '-q:v', '2', f'{out}/%04d.jpg'], check=True)
        print(f'{name:9} source {start:6.2f} s  {len(os.listdir(out))} images')


def timeline():
    t = INTRO
    segs = []
    for name, clock, at, dur, speed, cw, cap, sub in SEGMENTS:
        if not os.path.isdir(f'src/{name}'):
            continue
        segs.append(dict(name=name, t0=t, dur=dur, frames=len(os.listdir(f'src/{name}')), cap=cap, sub=sub))
        t += dur
    total = t + OUTRO
    json.dump(dict(segments=segs, intro=INTRO, outro=OUTRO, total=total, xf=XF, fps=FPS),
              open('timeline.json', 'w'), indent=1)
    print('durée', round(total, 2), 's')


if __name__ == '__main__':
    if EXTRACT:
        d0 = demo_start()
        print(f'démo lancée à {d0:.2f} s de l\'enregistrement')
        extract(d0)
    timeline()
