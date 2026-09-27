# Teaser

The teaser at the top of the README (`docs/assets/readme/teaser.*`) is cut from **real footage of
the app** — nothing in it is redrawn. The whole chain is scripted, so a new design only needs a
new run.

## Remake it

**Actions › Teaser › Run workflow** (app: `build` films this branch, `release` the latest
release). When it's done, download the **teaser** artifact and copy `teaser.mp4`, `teaser.webp`
and `teaser-poster.jpg` into `docs/assets/readme/`. `contact.png` shows one frame per shot, to
check the cut at a glance.

Locally, from a `footage` artifact: `tools/teaser/make.sh <footage dir> <out dir>`
(python3 + numpy + Pillow, node + Playwright with Chromium, ffmpeg with libwebp).

## How it works

1. **Film** — `footage/film.py`, on a Windows runner (job `film`): a sharp desktop (1920×1080 at
   175 %, animations on, `footage/wallpaper.jpg`, no icons, system pop-ups closed), a screen
   recording, `SpaceNotch.exe --demo`, then scripted input for what the demo doesn't do alone
   (Alt+Space, `12*8`, Escape twice). Writes `footage.mp4`, `spacenotch.log`, `clock.json`,
   `actions.log`.
2. **Cut** — `edit/cut.py` picks each shot (clock, start, length, speed, crop around the notch)
   and writes `timeline.json`. Demo shots are timed from `[DEMO] scénario lancé` in the app log,
   so a new take needs no re-timing; scripted shots use the recording clock.
3. **Titles** — `edit/edit.html` lays the shots end to end with dissolves, the captions, the intro
   and the outro; every frame is a pure function of time. Font: `edit/InterVariable.ttf`
   ([Inter](https://rsms.me/inter/), SIL Open Font License).
4. **Sound** — `edit/music.py` writes `music.wav` from `timeline.json`: chords change with the
   shots, effects in the same key.
5. **Render** — `edit/render.js` (Playwright → FFmpeg), then `make.sh` bakes the poster as frame 0,
   muxes the sound and encodes the README loop.

## Changing it

- **New UI, same story**: just rerun the workflow.
- **Different shots or captions**: edit `SEGMENTS` in `edit/cut.py` (the demo's own timings are in
  `src/SpaceNotch.Features/Demo/DemoScenario.cs`).
- **Different gestures**: edit the end of `footage/film.py` and the `rec` shots in `cut.py`.
