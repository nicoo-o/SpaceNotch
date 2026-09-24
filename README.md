<div align="center">

<img src="docs/assets/readme/hero.svg" alt="SpaceNotch — a notch that grows from the top of your screen, with a glowing pixel grid" width="100%">

<br>

<a href="https://github.com/nicoo-o/SpaceNotch/releases/latest/download/SpaceNotch.exe"><img src="https://img.shields.io/badge/Download_for_Windows-000000?style=for-the-badge&logo=windows11&logoColor=white" alt="Download for Windows" height="44"></a>

<br><br>

<a href="https://github.com/nicoo-o/SpaceNotch/releases/latest"><img src="https://img.shields.io/github/v/release/nicoo-o/SpaceNotch?style=flat-square&color=111111&label=release" alt="Latest release"></a>
<img src="https://img.shields.io/badge/Windows-11-111111?style=flat-square" alt="Windows 11">
<img src="https://img.shields.io/badge/license-MIT-111111?style=flat-square" alt="MIT license">
<img src="https://img.shields.io/badge/telemetry-none-111111?style=flat-square" alt="No telemetry">

<br>

**English** · [Français](README.fr.md)

</div>

<br>

<p align="center">
<em>A small piece of darkness at the top of your screen.<br>
It stays out of the way — and comes alive when something deserves your attention.</em>
</p>

<br>

## Meet your notch

<table>
  <tr>
    <td width="50%" valign="top"><img src="docs/assets/readme/state-music.jpg" alt="Compact notch playing music"><br><sub><b>Now playing</b> — artwork, title and a live level, nothing more</sub></td>
    <td width="50%" valign="top"><img src="docs/assets/readme/state-expanded.jpg" alt="Notch expanded into a music player"><br><sub><b>Click, and it grows</b> — the artwork morphs from the compact notch into the player</sub></td>
  </tr>
  <tr>
    <td width="50%" valign="top"><img src="docs/assets/readme/state-download.jpg" alt="Notch showing a download with a glowing pixel grid"><br><sub><b>Work you can feel</b> — a living pixel grid says something is happening</sub></td>
    <td width="50%" valign="top"><img src="docs/assets/readme/state-bubble.jpg" alt="Notch with a call bubble beside it"><br><sub><b>It splits for what matters</b> — calls, recordings and downloads get their own bubble; tap to swap</sub></td>
  </tr>
  <tr>
    <td width="50%" valign="top"><img src="docs/assets/readme/state-side.jpg" alt="Notch docked on the right edge as a slim tab"><br><sub><b>Any edge you like</b> — top, left or right, even on your second screen</sub></td>
    <td width="50%" valign="top"><img src="docs/assets/readme/state-floating.jpg" alt="Notch pulled off the edge, floating and stretching"><br><sub><b>Pull it off the edge</b> — it stretches, throws to corners and snaps back with a drop</sub></td>
  </tr>
</table>

<br>

## Alive, never busy

<p align="center">
<img src="docs/assets/readme/hypnotic.svg" alt="Eight glowing pixel-grid animations: read, think, search, process, sync, drop, complete, error" width="100%">
</p>

When something is working — a download, a search, a sync — a tiny grid of light breathes inside
the notch. Each kind of work has its own rhythm and colour. When it's done, the light settles into
a single calm pixel. When nothing is happening, nothing moves.

<br>

## Pull it off the edge

<p align="center">
<img src="docs/assets/readme/goo.jpg" alt="The notch being pulled down, stretching like a drop of ink, snapping free and becoming a floating pill" width="100%">
</p>

Grab the notch and pull. It resists, stretches like a drop of ink, and snaps free. Throw it to a
corner, park it on the side of your screen, carry it to your second monitor. Double-click, and it
flows back home.

<br>

## Made to disappear

<table>
  <tr>
    <td width="33%" valign="top"><h3>Quiet</h3>Nothing runs when nothing happens. No polling, no background animation — just a still shape at the top of your screen.</td>
    <td width="33%" valign="top"><h3>Respectful</h3>It steps aside when a game or a video goes fullscreen, and never pops open for a volume change.</td>
    <td width="33%" valign="top"><h3>Private</h3>No account, no telemetry, no network calls. Everything stays on your PC.</td>
  </tr>
  <tr>
    <td width="33%" valign="top"><h3>Deep black</h3>Pure OLED black with soft, concave shoulders, as if the screen itself had grown a little.</td>
    <td width="33%" valign="top"><h3>Gentle</h3>Honours Windows' <em>reduced motion</em> setting and speaks to Narrator.</td>
    <td width="33%" valign="top"><h3>Yours</h3>Colour, transparency, curves, edge, spring feel — tune it until it feels right.</td>
  </tr>
</table>

<br>

## What it shows

**Music** with artwork that grows into a player · **Volume & brightness** as a quiet overlay ·
**Downloads** from any browser · **Calls & recordings** from your mic and camera ·
**Notifications**, grouped by app · **Bluetooth** · **Timer & focus** · **A launcher** for your apps ·
**A shelf** for dragging files in and out · **Clipboard history** (off by default, swipe to delete) ·
**Plugins** for anything else.

<br>

## Get started

1. **[Download SpaceNotch.exe](https://github.com/nicoo-o/SpaceNotch/releases/latest/download/SpaceNotch.exe)** — one file, nothing to install.
2. Run it. Windows may say it *protected your PC*: choose **More info › Run anyway** (the app isn't code-signed yet).
3. Look up. Hover the notch to peek, click to open, right-click for the launcher.
   Settings live in the tray icon.

Want the full tour? Run `SpaceNotch.exe --demo`.

<sub>Windows 11 (23H2 or later), x64. Prefer a folder? Grab <code>SpaceNotch-win-x64.zip</code> from the <a href="https://github.com/nicoo-o/SpaceNotch/releases/latest">latest release</a>.</sub>

<br>

## Inspirations

The glowing pixel-grid animations — their shapes, colours and rhythms, and the falling binary
rain — are inspired by **[Hypnotizing UI](https://www.inspora.design/posts/hypnotizing-ui)**, featured
on Inspora. The original animation is not ours: SpaceNotch rebuilds the idea for Windows and
applies it to downloads, searches, syncs and every other kind of work in progress.

<br>

## For the curious

SpaceNotch is native C# on WinUI 3 and the Windows compositor — no browser engine inside.
Every shape you see above is drawn by the app's own geometry code, and every design choice is
written down.

[Technical overview](docs/development/project-overview.md) ·
[Design plan](docs/ux/spacenotch-2.0.md) ·
[Decisions](docs/decisions/README.md) ·
[Write a plugin](docs/plugin-api.md) ·
[Build from source](docs/development/building.md)

<br>

<div align="center">

<sub>MIT licensed · made with care for people who like their desktop calm.<br>
The desktop wallpaper in the screenshots was generated for this page.</sub>

<br><br>

<sub>If SpaceNotch made your screen a little nicer, a ⭐ helps others find it.</sub>

</div>
