#!/usr/bin/env bash
# Builds the teaser from a take: cut, soundtrack, titles, render, loop.
#
#   tools/teaser/make.sh <footage dir> <out dir>
#
# <footage dir> is the `footage` artifact of the « Teaser » workflow (footage.mp4,
# spacenotch.log, clock.json). <out dir> receives teaser.mp4, teaser.webp,
# teaser-poster.jpg and contact.png — the three first go to docs/assets/readme/.
# Needs python3 + numpy + Pillow, node + playwright (Chromium), ffmpeg with libwebp.
set -euo pipefail

FOOT=$(realpath "$1")
OUT=$(realpath -m "$2")
cd "$(dirname "$0")/edit"
mkdir -p "$OUT"
export NODE_PATH=${NODE_PATH:-$(npm root -g)}

python3 cut.py "$FOOT" extract
python3 music.py

# edit.html reads its frames and timeline over HTTP.
python3 -m http.server 8123 --bind 127.0.0.1 >/dev/null 2>&1 &
SERVER=$!
trap 'kill $SERVER 2>/dev/null || true' EXIT
sleep 1
kill -0 $SERVER 2>/dev/null || { echo "Le port 8123 est déjà pris : arrêtez l'autre serveur." >&2; exit 1; }

node render.js video.mp4

# Poster: the name settled over the first shot; it becomes frame 0 (every thumbnail shows it).
POSTER=$(python3 -c "import json; t=json.load(open('timeline.json')); s=t['segments'][0]; print(f\"{s['t0'] + s['dur'] * 0.7:.2f}\")")
node stills.js "$POSTER"
ffmpeg -y -loglevel error -i "still-$POSTER.png" -q:v 2 "$OUT/teaser-poster.jpg"

ffmpeg -y -loglevel error -i video.mp4 -loop 1 -t 0.0334 -i "still-$POSTER.png" -i music.wav \
  -filter_complex "[1:v]scale=1920:1080,format=yuv420p,setsar=1,fps=30[p];[0:v]trim=start_frame=1,setpts=PTS-STARTPTS[rest];[p][rest]concat=n=2:v=1:a=0[v]" \
  -map "[v]" -map 2:a -c:v libx264 -preset slow -crf 17 -pix_fmt yuv420p -c:a aac -b:a 192k -movflags +faststart -shortest \
  "$OUT/teaser.mp4"

# The README loop: autoplays, silent, light enough for a repository page.
ffmpeg -y -loglevel error -i video.mp4 -vf "fps=20,scale=1280:720:flags=lanczos,hqdn3d=2:1:3:3" \
  -c:v libwebp_anim -lossless 0 -q:v 85 -compression_level 6 -loop 0 -preset photo "$OUT/teaser.webp"

# Contact sheet: one frame per shot, to check the cut at a glance.
python3 - "$OUT" <<'PY'
import json, subprocess, sys
from PIL import Image
out = sys.argv[1]
t = json.load(open('timeline.json'))
times = [s['t0'] + s['dur'] * 0.6 for s in t['segments']]
subprocess.run(['node', 'stills.js', *[f'{x:.2f}' for x in times]], check=True)
sheet = Image.new('RGB', (1280, 360 * ((len(times) + 1) // 2)))
for i, x in enumerate(times):
    sheet.paste(Image.open(f'still-{x:.2f}.png').resize((640, 360)), ((i % 2) * 640, (i // 2) * 360))
sheet.save(f'{out}/contact.png')
PY

ls -la "$OUT"
