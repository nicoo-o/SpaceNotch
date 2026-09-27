# Teaser

The teaser at the top of the README (`docs/assets/readme/teaser.*`) is cut from **real footage of
the app** — nothing in it is redrawn.

1. **Filming** — `footage/film.py`, run by the *Tournage du teaser* workflow on a Windows runner:
   a sharp desktop (1920×1080 at 175 %, animations on, `footage/wallpaper.jpg`, no icons), a screen
   recording, `SpaceNotch.exe --demo`, then scripted input for what the demo doesn't do alone
   (Alt+Space and a quick calculation). The `footage` artifact holds `footage.mp4` and the logs.
2. **Cut** — `edit/cut.py <footage.mp4> extract` takes each shot from the footage (source time,
   speed, crop around the notch) and writes `timeline.json`. `edit/appear.py` finds when the notch
   first shows up, to re-time a new take.
3. **Titles** — `edit/edit.html` lays the shots end to end with dissolves, the captions, the intro
   and the outro; every frame is a pure function of time. Needs `InterVariable.ttf`
   ([Inter](https://rsms.me/inter/)) next to it, served over HTTP (`python3 -m http.server 8123`).
4. **Sound** — `edit/music.py` writes `music.wav` from `timeline.json`: chords change with the shots,
   effects in the same key.
5. **Render** — `edit/render.js` (Playwright → FFmpeg), then mux and loop:

```bash
NODE_PATH=$(npm root -g) node render.js video.mp4
ffmpeg -i video.mp4 -i music.wav -c:v libx264 -crf 16 -c:a aac -b:a 192k -shortest teaser.mp4
ffmpeg -i video.mp4 -vf "fps=20,scale=1280:720:flags=lanczos" -c:v libwebp_anim -q:v 92 -loop 0 -preset photo teaser.webp
```
