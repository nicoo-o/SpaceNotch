"""When does the notch first appear? Scans the top centre of the footage for black."""
import subprocess, sys
import numpy as np
src = sys.argv[1]
w, h = 400, 40
raw = subprocess.run(['ffmpeg', '-loglevel', 'error', '-i', src, '-t', '20', '-vf', f'fps=10,crop={w}:{h}:{(1920-w)//2}:0',
                      '-f', 'rawvideo', '-pix_fmt', 'gray', '-'], capture_output=True).stdout
frames = np.frombuffer(raw, np.uint8).reshape(-1, h, w)
base = frames[5].astype(int)   # the bare desk, after the recording settles
for i, f in enumerate(frames):
    if i > 5 and (abs(f.astype(int) - base) > 10).mean() > 0.10:
        print(f'{i / 10:.1f}')
        break
