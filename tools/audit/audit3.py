"""Troisième passage de l'audit runtime : essais ciblés, chacun repartant d'une notch
au bord haut, sur fond uni. Mesure seulement, ne corrige rien.

Usage : python audit3.py <SpaceNotch.exe> <dossier de sortie> <SpaceNotch.AuditProbe.dll>
"""
import ctypes
import glob
import hashlib
import json
import os
import shutil
import subprocess
import sys
import threading
import time
from ctypes import wintypes

import mss
import numpy as np
import psutil
import pyautogui
import win32api
import win32con
import win32gui
import win32process
from PIL import Image

EXE = os.path.abspath(sys.argv[1])
OUT = os.path.abspath(sys.argv[2])
PROBE = os.path.abspath(sys.argv[3])
SHOTS = os.path.join(OUT, "shots")
os.makedirs(SHOTS, exist_ok=True)
user32 = ctypes.windll.user32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
pyautogui.FAILSAFE = False
pyautogui.PAUSE = 0
LOCAL, ROAMING = os.environ["LOCALAPPDATA"], os.environ["APPDATA"]
LOG = os.path.join(LOCAL, "SpaceNotch", "logs", "spacenotch.log")
CONFIG = os.path.join(ROAMING, "SpaceNotch", "config.json")
PLUGINS = os.path.join(LOCAL, "SpaceNotch", "plugins")
BG = (200, 200, 200)
T0 = time.monotonic()
PHASE = "prep"
APP = None


def now():
    return round(time.monotonic() - T0, 3)


def record(test, status, **details):
    entry = {"t": now(), "phase": PHASE, "test": test, "status": status, "details": details}
    with open(os.path.join(OUT, "results3.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    print(f"{now():8.2f} [{PHASE}] {status:13} {test} {json.dumps(details, ensure_ascii=False)[:400]}", flush=True)


def phase(name):
    global PHASE
    PHASE = name
    print("=" * 20, name, flush=True)


def set_res(w, h):
    dm = win32api.EnumDisplaySettings(None, win32con.ENUM_CURRENT_SETTINGS)
    dm.PelsWidth, dm.PelsHeight = w, h
    dm.Fields = win32con.DM_PELSWIDTH | win32con.DM_PELSHEIGHT
    return win32api.ChangeDisplaySettings(dm, 0)


def plain_background():
    user32.SystemParametersInfoW(0x0014, 0, "", 3)
    user32.SetSysColors(1, (ctypes.c_int * 1)(1), (wintypes.DWORD * 1)(BG[0] | (BG[1] << 8) | (BG[2] << 16)))


def prepare_desktop():
    set_res(1920, 1080)
    user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(1), 3)
    user32.SystemParametersInfoW(0x103F, 0, ctypes.c_void_p(1), 3)
    plain_background()
    subprocess.run(["reg", "add", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                    "/v", "HideIcons", "/t", "REG_DWORD", "/d", "1", "/f"], capture_output=True)
    subprocess.run(["powershell", "-NoProfile", "-Command",
                    "Stop-Process -Name explorer -Force; Start-Sleep 3; (New-Object -ComObject Shell.Application).MinimizeAll()"],
                   capture_output=True)
    time.sleep(3)


def close_noise():
    while True:
        for title in ("System Properties", "Propriétés système", "Performance Options", "Virtual Memory"):
            hwnd = win32gui.FindWindow(None, title)
            if hwnd:
                win32gui.PostMessage(hwnd, win32con.WM_CLOSE, 0, 0)
        time.sleep(0.3)


threading.Thread(target=close_noise, daemon=True).start()


def read_log():
    try:
        with open(LOG, encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


def edit_config(**values):
    try:
        with open(CONFIG, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError):
        data = {}
    data.update(values)
    os.makedirs(os.path.dirname(CONFIG), exist_ok=True)
    with open(CONFIG, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def kill_app():
    global APP
    subprocess.run(["taskkill", "/F", "/IM", "SpaceNotch.exe"], capture_output=True)
    time.sleep(1.5)
    APP = None


def launch(*args, **config):
    """Relance propre : notch au bord haut, réglages donnés, journal neuf."""
    global APP
    kill_app()
    base = dict(WelcomeCompleted=True, DockEdge="Top", UpdateMode="Off", ShowPixel=False, Appearance="Dark",
                ApprovedPlugins={})
    base.update(config)
    edit_config(**base)
    if os.path.exists(LOG):
        try:
            os.remove(LOG)
        except OSError:
            pass
    t = time.monotonic()
    proc = subprocess.Popen([EXE, *args])
    APP = psutil.Process(proc.pid)
    ready = None
    while time.monotonic() - t < 60:
        if "IslandWindow prête" in read_log():
            ready = round(time.monotonic() - t, 2)
            break
        time.sleep(0.05)
    time.sleep(2)
    return ready


def windows_of(pid, visible=True):
    found = []

    def cb(hwnd, _):
        if win32process.GetWindowThreadProcessId(hwnd)[1] == pid and (not visible or win32gui.IsWindowVisible(hwnd)):
            found.append({"hwnd": hwnd, "title": win32gui.GetWindowText(hwnd), "rect": win32gui.GetWindowRect(hwnd),
                          "exstyle": hex(win32gui.GetWindowLong(hwnd, win32con.GWL_EXSTYLE) & 0xFFFFFFFF)})
        return True

    win32gui.EnumWindows(cb, None)
    return found


def island():
    for w in windows_of(APP.pid):
        if w["title"] == "SpaceNotch":
            return w
    return None


SW = SH = 0
grab = None


def refresh_screen():
    global SW, SH, grab
    SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
    grab = mss.mss()


def shape():
    width = min(1400, SW)
    reg = {"left": max(0, SW // 2 - width // 2), "top": 0, "width": width, "height": min(520, SH)}
    img = np.asarray(grab.grab(reg))[:, :, :3]
    dark = img.mean(axis=2) < 110
    top = dark[:6].any(axis=0)
    if not top.any():
        return 0, 0
    xs = np.where(top)[0]
    col = dark[:, (xs.min() + xs.max()) // 2]
    ys = np.where(col)[0]
    return int(xs.max() - xs.min() + 1), int(ys.max() + 1) if len(ys) else 0


def watch(seconds, action=None):
    series = []
    if action:
        threading.Thread(target=action, daemon=True).start()
    t0 = time.monotonic()
    while time.monotonic() - t0 < seconds:
        w, h = shape()
        series.append((round(time.monotonic() - t0, 4), w, h))
    return series


def settle_time(series, rest=None):
    if not series:
        return None
    final = series[-1][1:]
    first_change = next((s[0] for s in series if abs(s[2] - series[0][2]) > 2), None)
    last_change = None
    for s in reversed(series):
        if abs(s[1] - final[0]) > 2 or abs(s[2] - final[1]) > 2:
            last_change = s[0]
            break
    return {"start": first_change, "settle": last_change,
            "duration": round(last_change - first_change, 3) if first_change is not None and last_change is not None else None,
            "final": final, "max_h": max(s[2] for s in series)}


def shot(name, box=None):
    img = grab.grab({"left": 0, "top": 0, "width": SW, "height": SH})
    im = Image.frombytes("RGB", img.size, img.rgb)
    if box:
        im = im.crop(box)
    im.save(os.path.join(SHOTS, f"3-{len(os.listdir(SHOTS)):03d}-{name}.png"))


def top_crop():
    return (SW // 2 - 450, 0, SW // 2 + 450, 360)


def park():
    pyautogui.moveTo(SW - 150, SH // 2)


def click_notch():
    w, h = shape()
    pyautogui.moveTo(SW // 2, max(4, min(h // 2, 10)))
    time.sleep(0.05)
    pyautogui.click()


def foreground():
    hwnd = win32gui.GetForegroundWindow()
    try:
        name = psutil.Process(win32process.GetWindowThreadProcessId(hwnd)[1]).name()
    except Exception:
        name = "?"
    return hwnd, name


def notepad(maximized=False):
    subprocess.run(["taskkill", "/F", "/IM", "notepad.exe"], capture_output=True)
    p = subprocess.Popen(["notepad.exe"])
    time.sleep(2.5)
    hwnd = win32gui.FindWindow("Notepad", None)
    if not hwnd:
        for w in windows_of(p.pid):
            hwnd = w["hwnd"]
    try:
        if maximized:
            win32gui.ShowWindow(hwnd, win32con.SW_MAXIMIZE)
        else:
            win32gui.ShowWindow(hwnd, win32con.SW_SHOWNORMAL)
            win32gui.MoveWindow(hwnd, SW // 2 - 450, 300, 900, 500, True)
        win32gui.SetForegroundWindow(hwnd)
    except Exception:
        pass
    time.sleep(1)
    return hwnd


def cli(*args):
    p = subprocess.run([EXE, *args], capture_output=True, timeout=60)
    return p.returncode


def cpu_window(seconds):
    APP.cpu_percent(None)
    vals = []
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        time.sleep(1)
        vals.append(APP.cpu_percent(None) / psutil.cpu_count())
    a = np.array(vals)
    return {"mean": round(float(a.mean()), 3), "max": round(float(a.max()), 3), "seconds": len(vals)}


def mem():
    m = APP.memory_info()
    return {"rss_mb": round(m.rss / 2**20, 1), "private_mb": round(m.private / 2**20, 1),
            "threads": APP.num_threads(), "handles": APP.num_handles()}


def ps(cmd):
    p = subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True)
    return (p.stdout + p.stderr).strip()


# ==========================================================================
phase("environnement")
prepare_desktop()
refresh_screen()
record("env.screen", "INFO", screen=(SW, SH))
shutil.rmtree(os.path.join(LOCAL, "SpaceNotch"), ignore_errors=True)
shutil.rmtree(os.path.dirname(CONFIG), ignore_errors=True)

# --------------------------------------------------------------------------
phase("arret-propre")
for label, cfg in (("presentation-ouverte", {"WelcomeCompleted": False}), ("repos", {})):
    for attempt in range(2):
        ready = launch(**cfg)
        time.sleep(4)
        pid = APP.pid
        t = time.monotonic()
        subprocess.run(["taskkill", "/PID", str(pid)], capture_output=True)
        try:
            APP.wait(15)
            exited = round(time.monotonic() - t, 2)
        except psutil.TimeoutExpired:
            exited = None
        record(f"lifecycle.wm_close.{label}.{attempt}", "PASS" if exited else "FAIL", seconds=exited,
               closed_logged="IslandWindow fermée" in read_log(),
               windows_left=len(windows_of(pid, visible=False)) if psutil.pid_exists(pid) else 0)
        kill_app()

# --------------------------------------------------------------------------
phase("clic-a-travers")
ready = launch()
park()
time.sleep(3)
rest = shape()
isl = island()
atm = [w for w in windows_of(APP.pid) if "Atmosphere" in w["title"]]
record("clickthrough.layout", "INFO", rest=rest, island=isl, atmosphere=atm)
nh = notepad(maximized=True)
time.sleep(1)
ir = isl["rect"] if isl else (SW // 2 - 40, 0, SW // 2 + 40, 18)
points = {"sous_la_notch_dans_le_halo": (SW // 2, ir[3] + 20),
          "a_droite_dans_le_halo": (ir[2] + 30, 8),
          "a_gauche_dans_le_halo": (ir[0] - 30, 8),
          "loin_du_halo": (SW // 2, 400)}
for name, (x, y) in points.items():
    hit = win32gui.WindowFromPoint((x, y))
    try:
        proc = psutil.Process(win32process.GetWindowThreadProcessId(hit)[1]).name()
    except Exception:
        proc = "?"
    # Vrai clic : la barre des tâches d'abord au premier plan, puis clic ; Notepad doit passer devant.
    try:
        win32gui.SetForegroundWindow(win32gui.FindWindow("Shell_TrayWnd", None))
    except Exception:
        pass
    time.sleep(0.4)
    pyautogui.click(x, y)
    time.sleep(0.6)
    fg = foreground()
    record(f"clickthrough.{name}", "PASS" if fg[1].lower() == "notepad.exe" else "FAIL",
           point=(x, y), window_from_point=proc, foreground_after_click=fg[1],
           still_rest=shape() == rest)
    pyautogui.press("escape")
    time.sleep(0.5)
subprocess.run(["taskkill", "/F", "/IM", "notepad.exe"], capture_output=True)

# --------------------------------------------------------------------------
phase("focus")
ready = launch()
nh = notepad()
pyautogui.click(SW // 2, 500)
pyautogui.write("avant ", interval=0.03)
before = foreground()
cli("--progress", "--id", "boom", "--title", "Build", "--error")
time.sleep(0.4)
pyautogui.write("pendant", interval=0.05)
time.sleep(1.5)
after = foreground()
shot("erreur-pendant-la-frappe", top_crop())
# Le texte tapé est-il arrivé dans Notepad ?
edit = win32gui.FindWindowEx(nh, 0, None, None)
record("focus.high_priority_auto_open_steals_focus", "FAIL" if after[0] != before[0] else "PASS",
       before=before[1], after=after[1])
pyautogui.press("escape")
time.sleep(1)
click_notch()
time.sleep(1)
pyautogui.press("escape")
time.sleep(1.5)
record("focus.returns_after_click_escape", "PASS" if foreground()[0] == before[0] else "FAIL",
       after=foreground()[1])
subprocess.run(["taskkill", "/F", "/IM", "notepad.exe"], capture_output=True)

# --------------------------------------------------------------------------
phase("animations-reduites")
ready = launch()
park()
time.sleep(2)
rest = shape()
normal_open = settle_time(watch(2.5, click_notch))
normal_close = settle_time(watch(3.0, lambda: pyautogui.press("escape")))
user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(0), 3)
time.sleep(2)
park()
time.sleep(1)
rm_open = settle_time(watch(2.5, click_notch))
rm_close = settle_time(watch(3.0, lambda: pyautogui.press("escape")))
user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(1), 3)
record("reduced_motion.durations", "INFO", normal_open=normal_open, normal_close=normal_close,
       reduced_open=rm_open, reduced_close=rm_close)

# --------------------------------------------------------------------------
phase("theme")
for appearance in ("Auto", "Light"):
    for light in (1, 0):
        for name in ("AppsUseLightTheme", "SystemUsesLightTheme"):
            subprocess.run(["reg", "add", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                            "/v", name, "/t", "REG_DWORD", "/d", str(light), "/f"], capture_output=True)
        win32gui.SendMessageTimeout(win32con.HWND_BROADCAST, win32con.WM_SETTINGCHANGE, 0, "ImmersiveColorSet",
                                    win32con.SMTO_ABORTIFHUNG, 3000)
        launch(Appearance=appearance)
        park()
        cli("--notify", "--title", "Thème", "--body", f"{appearance} · Windows {'clair' if light else 'sombre'}")
        time.sleep(1.5)
        shot(f"theme-{appearance}-windows-{'clair' if light else 'sombre'}-carte", top_crop())
        click_notch()
        time.sleep(1.5)
        shot(f"theme-{appearance}-windows-{'clair' if light else 'sombre'}-ouverte", top_crop())
        pyautogui.press("escape")
        record(f"theme.{appearance}.windows_{'light' if light else 'dark'}", "INFO",
               log=[l for l in read_log().splitlines() if "Apparence" in l][-2:])

# Contraste élevé.
class HIGHCONTRAST(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.UINT), ("dwFlags", wintypes.DWORD), ("lpszDefaultScheme", wintypes.LPWSTR)]


launch()
park()
hc = HIGHCONTRAST(ctypes.sizeof(HIGHCONTRAST), 1, None)
user32.SystemParametersInfoW(0x0043, ctypes.sizeof(hc), ctypes.byref(hc), 3)
time.sleep(5)
cli("--notify", "--title", "Contraste élevé", "--body", "Lisible ?")
time.sleep(1.5)
shot("contraste-eleve-carte", top_crop())
click_notch()
time.sleep(1.5)
shot("contraste-eleve-ouverte", top_crop())
pyautogui.press("escape")
hc.dwFlags = 0
user32.SystemParametersInfoW(0x0043, ctypes.sizeof(hc), ctypes.byref(hc), 3)
time.sleep(4)
plain_background()
time.sleep(2)
record("high_contrast.alive", "INFO", alive=APP.is_running())

# --------------------------------------------------------------------------
phase("ecran")
launch()
park()
time.sleep(2)
before = island()
for (w, h) in ((1280, 720), (1920, 1080)):
    set_res(w, h)
    time.sleep(5)
    refresh_screen()
    plain_background()
    isl = island()
    cx = (isl["rect"][0] + isl["rect"][2]) / 2 if isl else None
    shot(f"resolution-{w}", (0, 0, SW, 300))
    record(f"display.resolution_{w}x{h}", "PASS" if isl and abs(cx - SW / 2) < 3 and isl["rect"][1] == 0 else "FAIL",
           island=isl, screen=(SW, SH))
# Ouverte pendant le changement.
click_notch()
time.sleep(0.4)
set_res(1280, 720)
time.sleep(4)
refresh_screen()
pyautogui.press("escape")
time.sleep(2)
isl = island()
record("display.resolution_change_while_open", "PASS" if isl and abs((isl["rect"][0] + isl["rect"][2]) / 2 - SW / 2) < 3 else "FAIL",
       island=isl, screen=(SW, SH))
set_res(1920, 1080)
time.sleep(4)
refresh_screen()
plain_background()
# Échelle à chaud.
before = island()
for steps in (1, 2, 0):
    user32.SystemParametersInfoW(0x009F, steps, None, 1)
    time.sleep(6)
    refresh_screen()
    isl = island()
    shot(f"echelle-{steps}", (SW // 2 - 300, 0, SW // 2 + 300, 120))
    record(f"display.dpi_step_{steps}", "INFO", island=isl, before=before, system_dpi=user32.GetDpiForSystem(),
           log=[l for l in read_log().splitlines() if "DPI" in l or "échelle" in l][-3:])
kill_app()
launch()
record("display.dpi_after_restart", "INFO", island=island())

# --------------------------------------------------------------------------
phase("pixel")
launch(ShowPixel=True)
park()
time.sleep(4)
rest = shape()
shot("pixel-repos", (SW // 2 - 200, 0, SW // 2 + 200, 80))
results = []
for cycle in range(3):
    time.sleep(36)
    dozing = shape()
    shot(f"pixel-assoupi-{cycle}", (SW // 2 - 200, 0, SW // 2 + 200, 80))
    pyautogui.moveTo(SW // 2 - 300, SH // 2, duration=0.3)
    time.sleep(3)
    awake = shape()
    shot(f"pixel-reveil-{cycle}", (SW // 2 - 200, 0, SW // 2 + 200, 80))
    park()
    results.append({"dozing": dozing, "awake": awake, "rest": rest})
record("pixel.doze_wake_cycles", "PASS" if all(abs(r["awake"][0] - rest[0]) <= 3 for r in results) else "FAIL",
       cycles=results, log=[l for l in read_log().splitlines() if "REPOS" in l or "ANIMATION" in l][:10])
# Survol au repos avec Pixel : l'heure s'installe.
pyautogui.moveTo(SW // 2, 8)
time.sleep(1.5)
shot("pixel-survol", (SW // 2 - 200, 0, SW // 2 + 200, 80))
park()
time.sleep(4)
record("pixel.hover_clock_then_eyes", "INFO", after=shape(), rest=rest)

# --------------------------------------------------------------------------
phase("languette")
launch()
park()
time.sleep(2)
pyautogui.moveTo(SW // 2, 8)
pyautogui.mouseDown()
time.sleep(0.1)
pyautogui.moveTo(SW // 2, 40, duration=0.15)
pyautogui.moveTo(SW - 5, SH // 2, duration=0.25)
pyautogui.mouseUp()
time.sleep(3)
tab = island()
shot("languette", (SW - 400, SH // 2 - 250, SW, SH // 2 + 250))
record("tab.created", "INFO", island=tab)
if tab:
    r = tab["rect"]
    pyautogui.click((r[0] + r[2]) // 2, (r[1] + r[3]) // 2)
    time.sleep(1.5)
    shot("languette-clic", (SW - 700, SH // 2 - 350, SW, SH // 2 + 350))
    record("tab.click", "INFO", island=island())
    pyautogui.press("escape")
    time.sleep(1.5)
    record("tab.escape", "INFO", island=island())
    r = island()["rect"]
    pyautogui.moveTo((r[0] + r[2]) // 2, (r[1] + r[3]) // 2)
    pyautogui.mouseDown()
    time.sleep(0.1)
    pyautogui.moveTo(r[0] - 40, (r[1] + r[3]) // 2, duration=0.2)
    pyautogui.moveTo(SW // 2, 3, duration=0.4)
    pyautogui.mouseUp()
    time.sleep(3)
    back = island()
    record("tab.drag_back_to_top", "PASS" if back and back["rect"][1] == 0 and abs((back["rect"][0] + back["rect"][2]) / 2 - SW / 2) < 30 else "FAIL",
           island=back)
    shot("languette-retour", top_crop())

# --------------------------------------------------------------------------
phase("demo")
launch("--demo")
park()
series = []
t = time.monotonic()
i = 0
while time.monotonic() - t < 80:
    series.append((round(time.monotonic() - t, 2), *shape()))
    if i % 60 == 0:
        shot(f"demo-{int(time.monotonic() - t):02d}s", top_crop())
    i += 1
with open(os.path.join(OUT, "series3-demo.json"), "w") as f:
    json.dump(series, f)
time.sleep(10)
rest_after = shape()
c = cpu_window(30)
record("demo.after", "INFO", final_shape=rest_after, cpu=c,
       log=[l for l in read_log().splitlines() if "[" in l and ("OMBRE" in l or "WARN" in l or "FATAL" in l)][:15])

# --------------------------------------------------------------------------
phase("endurance-1000")
launch()
park()
time.sleep(3)
rest = shape()
snap = {"0": mem()}
stuck = 0
t = time.monotonic()
for i in range(1, 1001):
    click_notch()
    time.sleep(0.3)
    pyautogui.press("escape")
    time.sleep(0.3)
    if i % 100 == 0:
        park()
        time.sleep(1.5)
        if abs(shape()[1] - rest[1]) > 3:
            stuck += 1
            shot(f"endurance-coincee-{i}", top_crop())
            pyautogui.press("escape")
            time.sleep(1)
        snap[str(i)] = mem()
park()
time.sleep(20)
snap["repos+20s"] = mem()
record("endurance.1000_open_close", "PASS" if stuck == 0 else "FAIL", minutes=round((time.monotonic() - t) / 60, 1),
       stuck=stuck, memory=snap, cpu_after=cpu_window(30))

# --------------------------------------------------------------------------
phase("greffons-signes")
os.makedirs(PLUGINS, exist_ok=True)
signed = os.path.join(PLUGINS, "SpaceNotch.AuditProbe.dll")
pfx = os.path.join(OUT, "attacker.pfx")
mk = ps("Import-Module Microsoft.PowerShell.Security; Import-Module PKI -ErrorAction SilentlyContinue;"
        "$c = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Audit Attacker' -CertStoreLocation Cert:\\CurrentUser\\My;"
        f"$p = ConvertTo-SecureString -String 'audit' -Force -AsPlainText; Export-PfxCertificate -Cert $c -FilePath '{pfx}' -Password $p | Out-Null; 'cert-ok'")
tools = sorted(glob.glob(r"C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe"))
shutil.copy(PROBE, signed)
sign = subprocess.run([tools[-1], "sign", "/fd", "SHA256", "/f", pfx, "/p", "audit", signed], capture_output=True, text=True) \
    if tools and os.path.exists(pfx) else None
status = ps(f"(Get-AuthenticodeSignature '{signed}').Status")
record("plugin.signing", "INFO", cert=mk[-300:], signtool=(sign.stdout + sign.stderr)[-300:] if sign else "absent",
       windows_status=status)
digest = hashlib.sha256(open(signed, "rb").read()).hexdigest().upper()
kill_app()
edit_config(ApprovedPlugins={"SpaceNotch.AuditProbe.dll": digest})
if os.path.exists(LOG):
    os.remove(LOG)
t = time.monotonic()
proc = subprocess.Popen([EXE])
APP = psutil.Process(proc.pid)
hung = probes = 0
while time.monotonic() - t < 20:
    for w in windows_of(APP.pid):
        res = wintypes.DWORD()
        ok = user32.SendMessageTimeoutW(w["hwnd"], 0, 0, 0, 0x0002, 250, ctypes.byref(res))
        probes += 1
        hung += 0 if ok else 1
        break
    time.sleep(0.1)
log = read_log()
record("plugin.self_signed_untrusted_loaded", "FAIL" if "Greffons chargés : 2" in log else "PASS",
       windows_status=status, log=[l for l in log.splitlines() if "Greffon" in l or "PLUGIN" in l][:6],
       note="FAIL = chargé alors que Windows juge la signature non approuvée")
record("plugin.slow_start_freezes_ui", "FAIL" if hung > 3 else "PASS", probes=probes, hung=hung,
       ready_line=[l for l in log.splitlines() if "prête" in l or "actives" in l])
cli("--progress", "--id", "victim", "--title", "Victime", "--percent", "50")
time.sleep(3)
shot("victime-avant", top_crop())
time.sleep(30)
shot("inondation", top_crop())
time.sleep(15)
shot("apres-effacement", top_crop())
record("plugin.removes_foreign_activity", "INFO", shape=shape(),
       note="capture apres-effacement : la progression Victime est-elle encore là ?")
kill_app()
# Signature altérée.
data = bytearray(open(signed, "rb").read())
pos = data.find("Sonde lente".encode("utf-16-le"))
if pos >= 0:
    data[pos] = ord("T")
    open(signed, "wb").write(bytes(data))
    tampered_status = ps(f"(Get-AuthenticodeSignature '{signed}').Status")
    edit_config(ApprovedPlugins={"SpaceNotch.AuditProbe.dll": hashlib.sha256(bytes(data)).hexdigest().upper()})
    if os.path.exists(LOG):
        os.remove(LOG)
    proc = subprocess.Popen([EXE])
    APP = psutil.Process(proc.pid)
    time.sleep(15)
    log = read_log()
    record("plugin.tampered_signature_loaded", "FAIL" if "Greffons chargés : 2" in log else "PASS",
           windows_status=tampered_status, log=[l for l in log.splitlines() if "Greffon" in l or "PLUGIN" in l][:6])
kill_app()
shutil.rmtree(PLUGINS, ignore_errors=True)

phase("fin")
if os.path.exists(LOG):
    shutil.copy(LOG, os.path.join(OUT, "log-audit3.txt"))
print("terminé", flush=True)
