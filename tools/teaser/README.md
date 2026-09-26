# Teaser

The 22-second teaser at the top of the README (`docs/assets/readme/teaser.*`), built frame by frame.

- `teaser.html` — every frame is a pure function of time (`render(t)`), drawn on a canvas with the app's own
  notch geometry, grid colours and copy. Needs `InterVariable.ttf` ([Inter](https://rsms.me/inter/)) next to it.
- `music.py` — the soundtrack (music and effects in one key), synthesised with NumPy → `music.wav`.
- `render.js` — renders every frame with Playwright and pipes it into FFmpeg; `stills.js` renders single frames for checks.

```bash
NODE_PATH=$(npm root -g) node render.js video.mp4
python3 music.py
ffmpeg -i video.mp4 -i music.wav -c:v copy -c:a aac -b:a 192k -shortest teaser.mp4
ffmpeg -i video.mp4 -vf "fps=20,scale=1280:720:flags=lanczos" -c:v libwebp_anim -q:v 92 -loop 0 -preset photo teaser.webp
```
