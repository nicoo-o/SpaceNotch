"""Films the real SpaceNotch on a Windows runner: demo scenario + scripted mouse and keyboard.

Prepares a clean, sharp desktop (high resolution, high scaling, animations on, own wallpaper,
no icons), starts a screen recording, launches `SpaceNotch.exe --demo`, then plays the
interactions the demo cannot: open the player, search with Alt+Space, pull the notch off the
edge. Every action is logged with its time since the recording started (`actions.log`).
"""
import ctypes
import os
import subprocess
import sys
import time
from ctypes import wintypes

import pyautogui
import win32api
import win32con

OUT = sys.argv[1]
EXE = sys.argv[2]
HERE = os.path.dirname(os.path.abspath(__file__))
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


user32.SetProcessDpiAware()
set_resolution()
set_scaling()
set_animations()
set_desktop()
time.sleep(2)

sw, sh = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
log(f"écran {sw}×{sh}")

# ---------- record ----------
rec = subprocess.Popen(["ffmpeg", "-y", "-loglevel", "error", "-f", "gdigrab", "-framerate", "30", "-draw_mouse", "1",
                        "-i", "desktop", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "10", "-pix_fmt", "yuv420p",
                        "-t", "70", OUT], stdin=subprocess.PIPE)
T0 = time.monotonic()
time.sleep(1.5)
log("enregistrement")

app = subprocess.Popen([EXE, "--demo"])
log("SpaceNotch --demo")

cx = sw // 2


def at(t):
    """Wait until t seconds after the recording started."""
    while time.monotonic() - T0 < t:
        time.sleep(0.01)


def glide(x, y, secs):
    pyautogui.moveTo(x, y, duration=secs, tween=pyautogui.easeInOutQuad)


# Rest the pointer far from the notch.
pyautogui.moveTo(cx + sw // 4, sh // 2)

# Open the player (music runs from the start of the demo; the volume shows at 3–4 s).
at(8.0); glide(cx, 14, 0.7); log("survol de la notch")
at(9.2); pyautogui.click(); log("clic : lecteur")
at(13.0); glide(cx + sw // 4, sh // 2, 0.8); log("le pointeur s'éloigne")

# Search once the demo has gone quiet (after 46 s the thinking card leaves).
at(49.0); pyautogui.hotkey("alt", "space"); log("Alt+Espace")
at(50.0)
for ch in "12*8":
    pyautogui.write(ch); time.sleep(0.16)
log("saisie 12*8")
at(53.2); pyautogui.press("escape"); log("Échap")

# Pull the notch off the edge, let it float, then send it home.
at(55.5); glide(cx, 12, 0.6); log("prise de la notch")
at(56.3); pyautogui.mouseDown(); log("appui")
glide(cx, 12 + sh // 3, 1.4); log("tirée vers le bas")
at(58.2); pyautogui.mouseUp(); log("relâchée : flottante")
at(59.0); glide(cx + sw // 4, sh // 2, 0.8)
at(61.5); glide(cx, 12 + sh // 3 + 6, 0.6)
at(62.4); pyautogui.doubleClick(); log("double-clic : retour au bord")
at(63.5); glide(cx + sw // 4, sh // 2, 0.8)

rec.wait()
log("fin")
app.kill()
with open(os.path.join(os.path.dirname(OUT), "actions.log"), "w", encoding="utf-8") as f:
    f.write("\n".join(log_lines) + "\n")
