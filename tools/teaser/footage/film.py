"""Films the real SpaceNotch on a Windows runner: demo scenario + scripted mouse and keyboard.

Prepares a clean, sharp desktop (high resolution, high scaling, animations on, own wallpaper,
no icons), starts a screen recording, launches `SpaceNotch.exe --demo`, then plays the
interactions the demo cannot: open the player, search with Alt+Space, pull the notch off the
edge. Every action is logged with its time since the recording started (`actions.log`).
"""
import ctypes
import json
import os
import shutil
import subprocess
import sys
import time
from ctypes import wintypes

import pyautogui
import threading

import win32api
import win32con
import win32gui

OUT = sys.argv[1]
EXE = sys.argv[2]
# « tour » : la visite de tous les états (--tour), filmée sans aucun geste.
TOUR = len(sys.argv) > 3 and sys.argv[3] == "tour"
DURATION = "170" if TOUR else "77"
HERE = os.path.dirname(os.path.abspath(__file__))
FFMPEG = shutil.which("ffmpeg")
user32 = ctypes.windll.user32
pyautogui.FAILSAFE = False
pyautogui.PAUSE = 0

log_lines = []


def log(msg):
    line = f"{time.monotonic() - T0:7.2f}  {msg}" if 'T0' in globals() else f"   prep  {msg}"
    print(line, flush=True)
    log_lines.append(line)


# ---------- a sharp desktop ----------
def set_resolution():
    for w, h in [(2560, 1440), (1920, 1080)]:
        dm = win32api.EnumDisplaySettings(None, win32con.ENUM_CURRENT_SETTINGS)
        dm.PelsWidth, dm.PelsHeight = w, h
        dm.Fields = win32con.DM_PELSWIDTH | win32con.DM_PELSHEIGHT
        if win32api.ChangeDisplaySettings(dm, 0) == win32con.DISP_CHANGE_SUCCESSFUL:
            log(f"résolution {w}×{h}")
            return
    log("résolution inchangée")


def set_scaling():
    # SPI_SETLOGICALDPIOVERRIDE (non documenté, utilisé par Paramètres) : pas relatif à l'échelle
    # recommandée. On monte jusqu'où Windows accepte.
    for steps in (4, 3, 2):
        if user32.SystemParametersInfoW(0x009F, steps, None, 1):
            log(f"échelle +{steps} crans")
            return
    log("échelle inchangée")


def set_animations():
    # SPI_SETCLIENTAREAANIMATION, SPI_SETUIEFFECTS : le runner les coupe ; la notch a besoin de ses ressorts.
    user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(1), 3)
    user32.SystemParametersInfoW(0x103F, 0, ctypes.c_void_p(1), 3)
    val = wintypes.BOOL()
    user32.SystemParametersInfoW(0x1042, 0, ctypes.byref(val), 0)
    log(f"animations : {bool(val.value)}")


def set_desktop():
    wall = os.path.join(HERE, "wallpaper.jpg")
    user32.SystemParametersInfoW(0x0014, 0, wall, 3)
    subprocess.run(["reg", "add", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "/v", "HideIcons", "/t", "REG_DWORD", "/d", "1", "/f"], capture_output=True)
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    "Stop-Process -Name explorer -Force; Start-Sleep 3; (New-Object -ComObject Shell.Application).MinimizeAll()"],
                   capture_output=True)
    log("bureau propre")


for step in (set_resolution, set_scaling, set_animations, set_desktop):
    try:
        step()
    except Exception as ex:  # un réglage manqué ne doit pas empêcher le tournage
        log(f"{step.__name__} : {ex}")
time.sleep(2)

# Pixels physiques partout : ce script (DPI par écran v2) et l'enregistreur (drapeau de
# compatibilité), sinon Windows leur donne un écran réduit à l'échelle et flou.
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
subprocess.run(["reg", "add", r"HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
                "/v", FFMPEG, "/t", "REG_SZ", "/d", "~ HIGHDPIAWARE", "/f"], capture_output=True)
sw, sh = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
log(f"écran {sw}×{sh}")

# Windows Server pops « System Properties » (paging file) at random: close it whenever it shows.
def close_noise():
    while True:
        for title in ("System Properties", "Propriétés système", "Performance Options", "Virtual Memory"):
            hwnd = win32gui.FindWindow(None, title)
            if hwnd:
                win32gui.PostMessage(hwnd, win32con.WM_CLOSE, 0, 0)
        time.sleep(0.3)


threading.Thread(target=close_noise, daemon=True).start()

# ---------- record ----------
rec = subprocess.Popen([FFMPEG, "-y", "-loglevel", "error", "-f", "gdigrab", "-framerate", "30", "-draw_mouse", "1",
                        "-i", "desktop", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-pix_fmt", "yuv420p",
                        "-t", DURATION, OUT], stdin=subprocess.PIPE)
T0 = time.monotonic()
T0_WALL = time.time()
time.sleep(1.5)
log("enregistrement")

app = subprocess.Popen([EXE, "--tour" if TOUR else "--demo"])
log("SpaceNotch --tour" if TOUR else "SpaceNotch --demo")

cx = sw // 2


def at(t):
    """Wait until t seconds after the recording started."""
    while time.monotonic() - T0 < t:
        time.sleep(0.01)


def glide(x, y, secs):
    pyautogui.moveTo(x, y, duration=secs, tween=pyautogui.easeInOutQuad)


# Rest the pointer far from the notch, and let the demo play untouched: music, volume,
# notifications, a download, AirPods, the grid at work. Opening the player here would
# hide all of it — an open notch stays open until you leave it.
pyautogui.moveTo(cx + sw // 4, sh // 2)

if TOUR:
    # La visite se joue seule : le pointeur reste loin, rien ne s'ouvre au survol.
    at(128.0)

else:
    # Search, once the demo is over (the music leaves at ~51.5 s). Windows only gives the
    # keyboard to a window the user just clicked: Alt+Space opens the search, a click on it
    # takes the focus, then we type.
    at(53.0); pyautogui.hotkey("alt", "space"); log("Alt+Espace")
    at(53.6); glide(cx, 20, 0.5)
    at(54.2); pyautogui.click(); log("clic : la recherche prend le clavier")
    at(54.8)
    for ch in "12*8":
        pyautogui.write(ch); time.sleep(0.18)
    log("saisie 12*8")
    at(56.2); glide(cx + sw // 4, sh // 2, 0.8)
    at(58.0); pyautogui.press("escape"); log("Échap : vide la recherche")
    at(58.5); pyautogui.press("escape"); log("Échap : la referme")

    # Pull the notch off the edge, once the search has fully folded back (a press during
    # that transition is ignored): hover, press, pull slowly, let it float, send it home.
    at(63.0); glide(cx, 10, 0.6); log("survol de la notch")
    at(64.2); pyautogui.mouseDown(); log("appui")
    glide(cx, 22, 0.35)
    glide(cx, 10 + sh // 3, 1.6); log("tirée vers le bas")
    at(66.6); pyautogui.mouseUp(); log("relâchée : flottante")
    at(67.2); glide(cx + sw // 4, sh // 2, 0.8)
    at(70.0); glide(cx, 10 + sh // 3 + 4, 0.6)
    at(70.9); pyautogui.doubleClick(); log("double-clic : retour au bord")
    at(71.8); glide(cx + sw // 4, sh // 2, 0.8)

rec.wait()
probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height",
                        "-of", "csv=p=0", OUT], capture_output=True, text=True)
log(f"fin — vidéo {probe.stdout.strip()} (ffmpeg : {FFMPEG})")
app.kill()
# The cut lines the demo up from this: where the recording started, in wall-clock time.
with open(os.path.join(os.path.dirname(OUT), "clock.json"), "w", encoding="utf-8") as f:
    json.dump({"recording_start_unix": T0_WALL}, f)
with open(os.path.join(os.path.dirname(OUT), "actions.log"), "w", encoding="utf-8") as f:
    f.write("\n".join(log_lines) + "\n")
