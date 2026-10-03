"""Banc d'essai runtime de SpaceNotch, sur un vrai Windows (runner GitHub).

Ne corrige rien : lance la vraie application, la pilote à la souris et au clavier,
provoque les cas limites, et mesure. Chaque essai écrit une ligne dans results.jsonl :
    {"test": ..., "status": "PASS|FAIL|INFO|NOT_MEASURED", "details": {...}}
Mesures continues : metrics.csv (CPU, mémoire, threads, handles, GDI/USER, connexions).
Forme de la notch : relevée à l'écran (pixels sombres sur fond gris uni), car la
fenêtre WinUI peut être plus grande que la forme dessinée.

Usage : python audit.py <SpaceNotch.exe> <dossier de sortie> [<TestPlugin.dll>]
"""
import ctypes
import csv
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
TEST_PLUGIN = sys.argv[3] if len(sys.argv) > 3 else None
os.makedirs(OUT, exist_ok=True)
SHOTS = os.path.join(OUT, "shots")
os.makedirs(SHOTS, exist_ok=True)

user32 = ctypes.windll.user32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
pyautogui.FAILSAFE = False
pyautogui.PAUSE = 0

LOCAL = os.environ["LOCALAPPDATA"]
ROAMING = os.environ["APPDATA"]
LOG = os.path.join(LOCAL, "SpaceNotch", "logs", "spacenotch.log")
CONFIG = os.path.join(ROAMING, "SpaceNotch", "config.json")
PLUGINS = os.path.join(LOCAL, "SpaceNotch", "plugins")
BG = (200, 200, 200)

T0 = time.monotonic()
PHASE = "prep"
APP = None  # psutil.Process
results = []
lock = threading.Lock()


def now():
    return round(time.monotonic() - T0, 3)


def say(msg):
    print(f"{now():8.2f} [{PHASE}] {msg}", flush=True)


def record(test, status, **details):
    entry = {"t": now(), "phase": PHASE, "test": test, "status": status, "details": details}
    with lock:
        results.append(entry)
        with open(os.path.join(OUT, "results.jsonl"), "a", encoding="utf-8") as f:
            f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    say(f"{status:13} {test} {json.dumps(details, ensure_ascii=False)[:300]}")


def phase(name):
    global PHASE
    PHASE = name
    say("=" * 20 + f" {name}")


# ------------------------------------------------------------------ environnement
def prepare_desktop():
    for w, h in [(1920, 1080)]:
        dm = win32api.EnumDisplaySettings(None, win32con.ENUM_CURRENT_SETTINGS)
        dm.PelsWidth, dm.PelsHeight = w, h
        dm.Fields = win32con.DM_PELSWIDTH | win32con.DM_PELSHEIGHT
        record("env.resolution", "INFO", requested=f"{w}x{h}",
               result=win32api.ChangeDisplaySettings(dm, 0))
    user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(1), 3)  # animations client
    user32.SystemParametersInfoW(0x103F, 0, ctypes.c_void_p(1), 3)  # effets UI
    # Fond uni : la forme de la notch se lit en pixels sombres.
    user32.SystemParametersInfoW(0x0014, 0, "", 3)
    elements = (ctypes.c_int * 1)(1)  # COLOR_DESKTOP
    colors = (wintypes.DWORD * 1)(BG[0] | (BG[1] << 8) | (BG[2] << 16))
    user32.SetSysColors(1, elements, colors)
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


def env_info():
    info = {
        "windows": subprocess.run(["cmd", "/c", "ver"], capture_output=True, text=True).stdout.strip(),
        "cpu": subprocess.run(["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor).Name"],
                              capture_output=True, text=True).stdout.strip(),
        "cores": psutil.cpu_count(),
        "ram_gb": round(psutil.virtual_memory().total / 2**30, 1),
        "gpu": subprocess.run(["powershell", "-NoProfile", "-Command",
                               "(Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name) -join '; '"],
                              capture_output=True, text=True).stdout.strip(),
        "screen": f"{user32.GetSystemMetrics(0)}x{user32.GetSystemMetrics(1)}",
        "monitors": user32.GetSystemMetrics(80),
        "dpi": user32.GetDpiForSystem(),
    }
    record("env.machine", "INFO", **info)


# ------------------------------------------------------------------ processus et fenêtres
def find_app(timeout=30):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        for p in psutil.process_iter(["name", "exe"]):
            try:
                if p.info["name"] and p.info["name"].lower() == "spacenotch.exe" and p.info["exe"] and \
                        os.path.normcase(p.info["exe"]) == os.path.normcase(EXE):
                    return p
            except psutil.Error:
                pass
        time.sleep(0.1)
    return None


def windows_of(pid):
    found = []

    def cb(hwnd, _):
        if win32process.GetWindowThreadProcessId(hwnd)[1] == pid:
            try:
                rect = win32gui.GetWindowRect(hwnd)
            except Exception:
                rect = None
            found.append({
                "hwnd": hwnd,
                "class": win32gui.GetClassName(hwnd),
                "title": win32gui.GetWindowText(hwnd),
                "visible": bool(win32gui.IsWindowVisible(hwnd)),
                "rect": rect,
                "style": hex(win32gui.GetWindowLong(hwnd, win32con.GWL_STYLE) & 0xFFFFFFFF),
                "exstyle": hex(win32gui.GetWindowLong(hwnd, win32con.GWL_EXSTYLE) & 0xFFFFFFFF),
            })
        return True

    win32gui.EnumWindows(cb, None)
    return found


def visible_windows():
    return [w for w in windows_of(APP.pid) if w["visible"]] if APP else []


def gui_resources(pid):
    h = ctypes.windll.kernel32.OpenProcess(0x1000, False, pid)  # QUERY_LIMITED_INFORMATION
    if not h:
        return None, None
    try:
        return user32.GetGuiResources(h, 0), user32.GetGuiResources(h, 1)
    finally:
        ctypes.windll.kernel32.CloseHandle(h)


def sampler():
    path = os.path.join(OUT, "metrics.csv")
    with open(path, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["t", "phase", "pid", "cpu_pct", "rss_mb", "private_mb", "threads", "handles", "gdi", "user",
                    "connections", "log_bytes"])
        last_pid = None
        while True:
            p = APP
            try:
                if p is None or not p.is_running():
                    time.sleep(0.5)
                    continue
                if p.pid != last_pid:
                    p.cpu_percent(None)
                    last_pid = p.pid
                    time.sleep(1)
                    continue
                cpu = p.cpu_percent(None) / psutil.cpu_count()
                mem = p.memory_info()
                gdi, usr = gui_resources(p.pid)
                conns = []
                try:
                    conns = [f"{c.raddr.ip}:{c.raddr.port}" for c in p.net_connections(kind="inet") if c.raddr]
                except psutil.Error:
                    pass
                for c in conns:
                    if c not in seen_remote:
                        seen_remote.add(c)
                        record("privacy.connection", "INFO", remote=c)
                w.writerow([now(), PHASE, p.pid, round(cpu, 3), round(mem.rss / 2**20, 2),
                            round(getattr(mem, "private", 0) / 2**20, 2), p.num_threads(), p.num_handles(),
                            gdi, usr, len(conns), os.path.getsize(LOG) if os.path.exists(LOG) else 0])
                f.flush()
            except psutil.Error:
                pass
            time.sleep(1)


seen_remote = set()
threading.Thread(target=sampler, daemon=True).start()
threading.Thread(target=close_noise, daemon=True).start()


def launch(*args, fresh_log=True, wait_ready=True):
    global APP
    if fresh_log and os.path.exists(LOG):
        try:
            os.remove(LOG)
        except OSError:
            pass
    t = time.monotonic()
    proc = subprocess.Popen([EXE, *args])
    APP = psutil.Process(proc.pid)
    ready = None
    if wait_ready:
        end = time.monotonic() + 60
        while time.monotonic() < end:
            if "IslandWindow prête" in read_log():
                ready = round(time.monotonic() - t, 3)
                break
            if not APP.is_running():
                break
            time.sleep(0.05)
    return proc, ready


def read_log():
    try:
        with open(LOG, encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return ""


def save_log(name):
    if os.path.exists(LOG):
        shutil.copy(LOG, os.path.join(OUT, name))


def kill_app():
    global APP
    for p in psutil.process_iter(["name"]):
        if (p.info["name"] or "").lower() == "spacenotch.exe":
            try:
                p.kill()
            except psutil.Error:
                pass
    time.sleep(1.5)
    APP = None


def quit_gracefully(timeout=15):
    """taskkill sans /F : WM_CLOSE aux fenêtres de premier niveau, comme une fermeture de session."""
    if APP is None:
        return None
    pid = APP.pid
    t = time.monotonic()
    subprocess.run(["taskkill", "/PID", str(pid)], capture_output=True)
    try:
        APP.wait(timeout)
        return round(time.monotonic() - t, 2)
    except psutil.TimeoutExpired:
        return None


# ------------------------------------------------------------------ lecture de la forme à l'écran
SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
grab = mss.mss()


def region():
    width = min(1400, SW)
    return {"left": max(0, SW // 2 - width // 2), "top": 0, "width": width, "height": min(520, SH)}


def shape(img=None):
    """(largeur, hauteur, bbox) des pixels nettement plus sombres que le fond, autour du centre haut."""
    reg = region()
    if img is None:
        img = np.asarray(grab.grab(reg))[:, :, :3][:, :, ::-1]
    lum = img.mean(axis=2)
    dark = lum < 110
    rows = np.where(dark.any(axis=1))[0]
    cols = np.where(dark.any(axis=0))[0]
    if len(rows) == 0:
        return 0, 0, None
    # Le bloc sombre principal qui touche le haut de l'écran (ou le plus grand si détaché).
    top_band = dark[: min(6, dark.shape[0])].any(axis=0)
    xs = np.where(top_band)[0] if top_band.any() else cols
    x0, x1 = int(xs.min()), int(xs.max())
    center_col = dark[:, (x0 + x1) // 2]
    ys = np.where(center_col)[0]
    h = int(ys.max()) + 1 if len(ys) else 0
    return x1 - x0 + 1, h, (x0 + reg["left"], 0, x1 + reg["left"], h)


def full_shape_bbox():
    reg = {"left": 0, "top": 0, "width": SW, "height": SH - 60}
    img = np.asarray(grab.grab(reg))[:, :, :3][:, :, ::-1]
    dark = img.mean(axis=2) < 110
    ys, xs = np.where(dark)
    if len(xs) == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def shot(name):
    path = os.path.join(SHOTS, f"{len(os.listdir(SHOTS)):03d}-{name}.png")
    img = grab.grab({"left": 0, "top": 0, "width": SW, "height": SH})
    Image.frombytes("RGB", img.size, img.rgb).save(path)
    return path


def watch(seconds, label=None, action=None):
    """Relève la forme aussi vite que possible pendant `seconds`. Rend [(t, w, h)]."""
    series = []
    end = time.monotonic() + seconds
    if action:
        threading.Thread(target=action, daemon=True).start()
    t0 = time.monotonic()
    while time.monotonic() < end:
        w, h, _ = shape()
        series.append((round(time.monotonic() - t0, 4), w, h))
    if label:
        with open(os.path.join(OUT, f"series-{label}.json"), "w") as f:
            json.dump(series, f)
    return series


def analyse(series, rest):
    """Durée de transition, stabilité finale, à-coups (paliers et sauts) d'une série."""
    if not series:
        return {}
    dt = np.diff([s[0] for s in series]) if len(series) > 1 else np.array([0])
    hs = np.array([s[2] for s in series])
    ws = np.array([s[1] for s in series])
    changed = np.where((np.abs(hs - hs[0]) > 2) | (np.abs(ws - ws[0]) > 2))[0]
    start = series[changed[0]][0] if len(changed) else None
    final = (int(ws[-1]), int(hs[-1]))
    settle = None
    for i in range(len(series) - 1, -1, -1):
        if abs(ws[i] - final[0]) > 2 or abs(hs[i] - final[1]) > 2:
            settle = series[min(i + 1, len(series) - 1)][0]
            break
    # À-coups : pendant le mouvement, un palier de plus de 50 ms puis reprise.
    stalls = 0
    if start is not None and settle is not None:
        moving = [(t, w, h) for t, w, h in series if start <= t <= settle]
        run_start = None
        for i in range(1, len(moving)):
            same = moving[i][1] == moving[i - 1][1] and moving[i][2] == moving[i - 1][2]
            if same and run_start is None:
                run_start = moving[i - 1][0]
            elif not same and run_start is not None:
                if moving[i][0] - run_start > 0.05:
                    stalls += 1
                run_start = None
    return {
        "samples": len(series),
        "sample_ms_median": round(float(np.median(dt)) * 1000, 1),
        "start_s": start,
        "settle_s": settle,
        "duration_s": round(settle - start, 3) if start is not None and settle is not None else None,
        "final": final,
        "back_to_rest": rest is not None and abs(final[0] - rest[0]) <= 3 and abs(final[1] - rest[1]) <= 3,
        "max_h": int(hs.max()),
        "max_w": int(ws.max()),
        "stalls_over_50ms": stalls,
    }


def rest_shape(wait=1.0):
    time.sleep(wait)
    w, h, box = shape()
    return (w, h), box


def park():
    pyautogui.moveTo(SW // 2 + SW // 3, SH // 2)


def notch_center(box):
    if box is None:
        return SW // 2, 6
    return (box[0] + box[2]) // 2, max(4, min(box[3] // 2, 12))


def foreground():
    hwnd = win32gui.GetForegroundWindow()
    try:
        pid = win32process.GetWindowThreadProcessId(hwnd)[1]
        name = psutil.Process(pid).name()
    except Exception:
        name = "?"
    return hwnd, name, win32gui.GetWindowText(hwnd)


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


def cli(*args):
    t = time.monotonic()
    p = subprocess.run([EXE, *args], capture_output=True, timeout=30)
    return p.returncode, round(time.monotonic() - t, 2)


def cpu_window(seconds):
    """CPU moyen et max sur une fenêtre, mesuré ici (pas via metrics.csv) : en % d'un PC entier."""
    if APP is None:
        return None
    APP.cpu_percent(None)
    vals = []
    t_end = time.monotonic() + seconds
    while time.monotonic() < t_end:
        time.sleep(1)
        try:
            vals.append(APP.cpu_percent(None) / psutil.cpu_count())
        except psutil.Error:
            break
    if not vals:
        return None
    a = np.array(vals)
    return {"mean": round(float(a.mean()), 3), "p95": round(float(np.percentile(a, 95)), 3),
            "max": round(float(a.max()), 3), "zero_seconds_pct": round(float((a < 0.05).mean() * 100), 1),
            "seconds": len(vals)}


def mem():
    if APP is None:
        return None
    m = APP.memory_info()
    gdi, usr = gui_resources(APP.pid)
    return {"rss_mb": round(m.rss / 2**20, 1), "private_mb": round(m.private / 2**20, 1),
            "threads": APP.num_threads(), "handles": APP.num_handles(), "gdi": gdi, "user": usr}


def log_findings(tag):
    text = read_log()
    lines = text.splitlines()
    bad = [l for l in lines if any(k in l for k in ("[FATAL]", "[WARN]", "Exception", "[OMBRE]", "échoué", "[ANIMATION]",
                                                      "[REPOS]", "[GEOMETRIE]", "[CONFIG]", "[PLUGIN]", "[ACTION]",
                                                      "[MISE À JOUR]"))]
    record(f"log.{tag}", "INFO", lines=len(lines), notable=len(bad), sample=bad[:60])
    return lines


# ================================================================== SCÉNARIO
phase("environnement")
prepare_desktop()
# La taille de l'écran se relit APRÈS le changement de résolution (sinon on capture 1024×768).
SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
grab = mss.mss()
env_info()
for d in (os.path.dirname(CONFIG), os.path.join(LOCAL, "SpaceNotch")):
    shutil.rmtree(d, ignore_errors=True)

# ---------------------------------------------------------------- 1. premier lancement
phase("demarrage-a-froid")
park()
proc, ready = launch()
record("startup.ready_seconds", "INFO" if ready else "FAIL", seconds=ready)
time.sleep(2)
wins = windows_of(APP.pid)
record("startup.windows", "INFO", windows=wins)
shot("premier-lancement-2s")
time.sleep(6)
shot("premier-lancement-8s")
w, h, box = shape()
record("startup.first_run_shape", "INFO", width=w, height=h, box=box)
time.sleep(4)
shot("premier-lancement-presentation")
log_findings("first_run")
# La présentation du premier lancement : Échap la referme ?
pyautogui.press("escape")
time.sleep(2)
shot("apres-echap-presentation")
save_log("log-01-premier-lancement.txt")
t_quit = quit_gracefully()
record("lifecycle.graceful_quit", "PASS" if t_quit else "FAIL", seconds=t_quit,
       shutdown_logged="IslandWindow fermée" in read_log(), shadow_summary=[l for l in read_log().splitlines() if "OMBRE" in l or "écart" in l][-3:])
save_log("log-02-arret.txt")
kill_app()

# Les essais suivants partent d'un utilisateur qui a vu la présentation.
edit_config(WelcomeCompleted=True, EnableDiagnostics=True, ShowPixel=False, PixelDefaultApplied=True)

# ---------------------------------------------------------------- 2. repos silencieux
phase("repos-sans-pixel")
proc, ready = launch()
record("startup.warm_ready_seconds", "INFO" if ready else "FAIL", seconds=ready)
park()
time.sleep(10)
rest, rest_box = rest_shape()
record("rest.shape", "INFO", width=rest[0], height=rest[1], box=rest_box, windows=visible_windows())
shot("repos")
m0 = mem()
record("idle.memory_start", "INFO", **(m0 or {}))
log_before = os.path.getsize(LOG) if os.path.exists(LOG) else 0
c = cpu_window(170)
log_after = os.path.getsize(LOG) if os.path.exists(LOG) else 0
record("idle.cpu_no_activity", "PASS" if c and c["mean"] < 0.2 else "FAIL", **(c or {}), log_bytes_written=log_after - log_before)
record("idle.memory_end", "INFO", **(mem() or {}))

# ---------------------------------------------------------------- 3. survol : seuil de l'aperçu
phase("survol")
cx, cy = notch_center(rest_box)
for ms in (80, 150, 200, 260, 400, 700, 1200):
    park()
    time.sleep(1.8)
    pyautogui.moveTo(cx, cy)
    series = watch(ms / 1000)
    park()
    after = watch(2.5)
    grew = any(s[2] > rest[1] + 3 or s[1] > rest[0] + 3 for s in series + after)
    record(f"hover.dwell_{ms}ms", "INFO", preview_shown=grew, max_h=max(s[2] for s in series + after),
           back_to_rest=abs(after[-1][2] - rest[1]) <= 3 and abs(after[-1][1] - rest[0]) <= 3)
time.sleep(2)

# Survol prolongé, mouvement dans la zone, sortie.
park(); time.sleep(2)
pyautogui.moveTo(cx, cy)
s_in = watch(2.0, "hover-hold")
for dx in range(-40, 41, 8):
    pyautogui.moveTo(cx + dx, cy + 4)
    time.sleep(0.03)
s_move = watch(0.8)
park()
s_out = watch(3.5, "hover-leave")
a_in, a_out = analyse(s_in, rest), analyse(s_out, rest)
record("hover.preview_open", "INFO", **a_in)
record("hover.preview_stays_while_moving", "PASS" if s_move and s_move[-1][2] >= a_in.get("max_h", 0) - 3 else "FAIL",
       h_during_move=[s[2] for s in s_move[::10]])
record("hover.preview_close_after_leave", "PASS" if a_out.get("back_to_rest") else "FAIL", **a_out)

# Entrées et sorties en rafale (40 ms) : aucun aperçu ne doit naître, aucun état coincé.
park(); time.sleep(2)
for i in range(30):
    pyautogui.moveTo(cx, cy); time.sleep(0.04)
    pyautogui.moveTo(cx, cy + 140); time.sleep(0.04)
park()
s = watch(3.0, "hover-burst")
record("hover.rapid_in_out_returns_to_rest", "PASS" if analyse(s, rest).get("back_to_rest") else "FAIL",
       **analyse(s, rest))

# Passage accidentel : on traverse la notch d'un côté à l'autre.
park(); time.sleep(2)
pyautogui.moveTo(cx - 400, 8)
s = watch(1.5, "hover-crossing", action=lambda: pyautogui.moveTo(cx + 400, 8, duration=0.35))
record("hover.crossing_no_open", "PASS" if max(x[2] for x in s) <= rest[1] + 3 else "FAIL",
       max_h=max(x[2] for x in s), rest_h=rest[1])
park(); time.sleep(3)

# ---------------------------------------------------------------- 4. clic, Échap, interruptions
phase("ouverture-fermeture")
fg_before = foreground()


def click_open():
    pyautogui.moveTo(cx, cy)
    time.sleep(0.05)
    pyautogui.click()


s_open = watch(2.5, "click-open", action=click_open)
a_open = analyse(s_open, rest)
record("click.open", "PASS" if a_open.get("max_h", 0) > rest[1] + 20 else "FAIL", **a_open)
shot("ouverte")
fg_open = foreground()
record("focus.after_click_open", "INFO", before=fg_before[1:], after=fg_open[1:])
s_close = watch(2.5, "escape-close", action=lambda: pyautogui.press("escape"))
a_close = analyse(s_close, rest)
record("escape.close", "PASS" if a_close.get("back_to_rest") else "FAIL", **a_close)
park()
time.sleep(3)
w_, h_, _ = shape()
record("escape.final_rest", "PASS" if abs(h_ - rest[1]) <= 3 else "FAIL", height=h_, rest=rest[1])

# Fermeture par clic ailleurs.
click_open(); time.sleep(1.2)
pyautogui.click(SW // 2 + 500, SH // 2)
s = watch(3.0, "click-outside")
record("click_outside.close", "PASS" if analyse(s, rest).get("back_to_rest") else "FAIL", **analyse(s, rest))
park(); time.sleep(2)

# Second clic sur la notch ouverte.
click_open(); time.sleep(1.2)
s = watch(3.0, "second-click", action=click_open)
record("click.second_click_closes", "PASS" if analyse(s, rest).get("back_to_rest") else "FAIL", **analyse(s, rest))
park(); time.sleep(2)

# Échap à différents moments de l'ouverture.
for delay in (0.03, 0.08, 0.15, 0.3, 0.5):
    park(); time.sleep(1.5)

    def act(d=delay):
        click_open()
        time.sleep(d)
        pyautogui.press("escape")

    s = watch(3.5, f"interrupt-esc-{int(delay*1000)}", action=act)
    a = analyse(s, rest)
    record(f"interrupt.escape_after_{int(delay*1000)}ms", "PASS" if a.get("back_to_rest") else "FAIL", **a)

# Clics en rafale : A → B → A → B (8 clics à 90 ms).
park(); time.sleep(2)


def burst():
    pyautogui.moveTo(cx, cy)
    for _ in range(8):
        pyautogui.click(); time.sleep(0.09)


s = watch(5.0, "click-burst", action=burst)
a = analyse(s, rest)
park(); time.sleep(3)
w_, h_, _ = shape()
record("interrupt.click_burst_8x90ms", "INFO", final_after_burst=(w_, h_), rest=rest, **a)
if abs(h_ - rest[1]) > 3:
    pyautogui.press("escape"); time.sleep(2)

# Double-clic.
park(); time.sleep(2)
pyautogui.moveTo(cx, cy)
s = watch(3.0, "double-click", action=lambda: pyautogui.doubleClick())
record("click.double_click", "INFO", **analyse(s, rest))
pyautogui.press("escape"); park(); time.sleep(2)

# Clic droit : menu rapide.
pyautogui.moveTo(cx, cy)
s = watch(2.0, "right-click", action=lambda: pyautogui.rightClick())
shot("clic-droit")
record("click.right_click", "INFO", **analyse(s, rest), windows=[w for w in visible_windows()])
pyautogui.press("escape"); time.sleep(0.5); pyautogui.press("escape"); park(); time.sleep(2)

# Molette au repos.
pyautogui.moveTo(cx, cy)
s = watch(2.0, "wheel", action=lambda: pyautogui.scroll(-240))
record("wheel.at_rest", "INFO", **analyse(s, rest))
park(); time.sleep(2)

# ---------------------------------------------------------------- 5. focus
phase("focus")
note = subprocess.Popen(["notepad.exe"])
time.sleep(2.5)
nh = None
for w in windows_of(note.pid) or []:
    if w["visible"]:
        nh = w["hwnd"]
if nh is None:
    nh = win32gui.FindWindow("Notepad", None)
try:
    win32gui.ShowWindow(nh, win32con.SW_SHOWNORMAL)
    win32gui.MoveWindow(nh, SW // 2 - 450, 300, 900, 500, True)
    win32gui.SetForegroundWindow(nh)
except Exception as ex:
    record("focus.notepad", "INFO", error=str(ex))
time.sleep(1)
pyautogui.click(SW // 2, 500)
pyautogui.write("abc", interval=0.03)
before = foreground()
# Survol : ne doit jamais prendre le focus.
pyautogui.moveTo(cx, cy); time.sleep(1.5)
after_hover = foreground()
record("focus.hover_keeps_foreground", "PASS" if after_hover[0] == before[0] else "FAIL",
       before=before[1:], after=after_hover[1:])
park(); time.sleep(2.5)
# Activité qui arrive pendant la frappe.
code, secs = cli("--notify", "--title", "Audit", "--body", "Pendant la frappe")
pyautogui.write("def", interval=0.05)
time.sleep(1.5)
after_notify = foreground()
record("focus.notification_keeps_foreground", "PASS" if after_notify[0] == before[0] else "FAIL",
       cli_exit=code, cli_seconds=secs, after=after_notify[1:])
time.sleep(6)
# Clic sur la notch, puis Échap : le focus revient-il à l'application ?
click_open(); time.sleep(1.2)
during = foreground()
pyautogui.press("escape"); time.sleep(1.5)
after_close = foreground()
record("focus.returns_after_click_escape", "PASS" if after_close[0] == before[0] else "FAIL",
       during=during[1:], after=after_close[1:])
# La frappe atteint-elle encore Notepad ?
pyautogui.click(SW // 2, 500)
pyautogui.write("ghi", interval=0.03)
time.sleep(0.5)
# Clic à côté de la notch, dans le rectangle de sa fenêtre mais hors de la forme.
island = [w for w in visible_windows() if w["rect"] and w["rect"][1] <= 0]
record("window.island_rects", "INFO", windows=island, rest_box=rest_box)
win32gui.MoveWindow(nh, 0, 0, SW, SH - 60, True)
time.sleep(1)
targets = []
if rest_box:
    targets = [(rest_box[2] + 25, 6), (rest_box[0] - 25, 6), (cx, rest_box[3] + 12), (cx, rest_box[3] + 40),
               (rest_box[2] + 80, rest_box[3] + 30)]
for (x, y) in targets:
    hit = win32gui.WindowFromPoint((x, y))
    try:
        hit_pid = win32process.GetWindowThreadProcessId(hit)[1]
        hit_name = psutil.Process(hit_pid).name()
    except Exception:
        hit_name = "?"
    record("clickthrough.window_from_point", "PASS" if hit_name.lower() != "spacenotch.exe" else "FAIL",
           point=(x, y), window_class=win32gui.GetClassName(hit), process=hit_name)
# Clic réel au bord du halo : Notepad doit le recevoir (il redevient premier plan s'il ne l'était plus).
try:
    win32gui.SetForegroundWindow(win32gui.FindWindow("Shell_TrayWnd", None))
except Exception:
    pass
time.sleep(0.5)
if rest_box:
    pyautogui.click(cx, rest_box[3] + 30)
    time.sleep(0.8)
    fg = foreground()
    record("clickthrough.real_click_under_halo", "PASS" if fg[1].lower().startswith("notepad") else "FAIL",
           foreground=fg[1:])
subprocess.run(["taskkill", "/F", "/IM", "notepad.exe"], capture_output=True)
park(); time.sleep(2)

# ---------------------------------------------------------------- 6. activités concurrentes
phase("activites")
log_mark = len(read_log())
procs = [subprocess.Popen([EXE, *a]) for a in (
    ["--progress", "--id", "dl", "--title", "Téléchargement", "--percent", "10"],
    ["--notify", "--title", "Message", "--body", "Arrivé en même temps"],
    ["--progress", "--id", "build", "--title", "Build", "--step", "1/4"],
    ["--notify", "--title", "Second", "--body", "Encore un"],
)]
for p in procs:
    p.wait(30)
s = watch(4.0, "concurrent-arrivals")
shot("activites-simultanees")
record("activity.concurrent_arrivals", "INFO", **analyse(s, rest), new_log=read_log()[log_mark:][-1500:])
for pct in (30, 60, 90):
    cli("--progress", "--id", "dl", "--title", "Téléchargement", "--percent", str(pct))
    time.sleep(0.4)
shot("progression-90")
cli("--progress", "--id", "dl", "--title", "Téléchargement", "--done")
cli("--progress", "--id", "build", "--title", "Build", "--error")
time.sleep(1)
shot("termine-et-erreur")
# Combien de temps avant le retour au repos, sans rien toucher ?
t_wait = time.monotonic()
back = None
while time.monotonic() - t_wait < 90:
    w_, h_, _ = shape()
    if abs(h_ - rest[1]) <= 3 and abs(w_ - rest[0]) <= 3:
        back = round(time.monotonic() - t_wait, 1)
        break
    time.sleep(0.5)
shot("apres-activites")
record("activity.returns_to_rest_unattended", "PASS" if back is not None else "FAIL", seconds=back)
cli("--progress", "--id", "build", "--clear")
cli("--progress", "--id", "dl", "--clear")
time.sleep(2)

# Rafale : 40 notifications aussi vite que possible.
mark_mem = mem()
t = time.monotonic()
spam = [subprocess.Popen([EXE, "--notify", "--title", f"Rafale {i}", "--body", "x" * (i % 7)]) for i in range(40)]
for p in spam:
    p.wait(60)
spam_cpu = cpu_window(10)
record("activity.burst_40_notifications", "INFO", launch_seconds=round(time.monotonic() - t, 1), cpu=spam_cpu,
       mem_before=mark_mem, mem_after=mem())
shot("apres-rafale")
t_wait = time.monotonic()
back = None
while time.monotonic() - t_wait < 240:
    w_, h_, _ = shape()
    if abs(h_ - rest[1]) <= 3 and abs(w_ - rest[0]) <= 3:
        back = round(time.monotonic() - t_wait, 1)
        break
    time.sleep(1)
record("activity.burst_drains", "PASS" if back is not None else "FAIL", seconds=back)
# Message invalide sur le canal : la notch survit ?
code, _ = cli("--progress")
code2, _ = cli("--notify", "--title", "")
code3, _ = cli("--progress", "--id", "x" * 5000, "--percent", "NaN")
time.sleep(2)
record("activity.invalid_messages", "PASS" if APP.is_running() else "FAIL", exits=(code, code2, code3))
cli("--progress", "--id", ("x" * 5000).lower(), "--clear")

# Retour au silence après les activités (état C).
park()
time.sleep(15)
c = cpu_window(60)
record("idle.cpu_after_activities", "PASS" if c and c["mean"] < 0.2 else "FAIL", **(c or {}))

# ---------------------------------------------------------------- 7. endurance : 300 ouvertures/fermetures
phase("endurance")
mems = {"0": mem()}
stuck = 0
for i in range(1, 301):
    click_open()
    time.sleep(0.45)
    pyautogui.press("escape")
    time.sleep(0.45)
    if i % 25 == 0:
        park(); time.sleep(1.5)
        w_, h_, _ = shape()
        if abs(h_ - rest[1]) > 3:
            stuck += 1
            shot(f"coincee-{i}")
            pyautogui.press("escape"); time.sleep(1.5)
    if i in (100, 200, 300):
        mems[str(i)] = mem()
record("endurance.300_open_close", "PASS" if stuck == 0 else "FAIL", stuck_checks=stuck, memory=mems)
# 300 survols en rafale.
for i in range(300):
    pyautogui.moveTo(cx, cy); time.sleep(0.02)
    pyautogui.moveTo(cx + 300, cy + 200); time.sleep(0.02)
park(); time.sleep(4)
w_, h_, _ = shape()
record("endurance.300_hover_flicks", "PASS" if abs(h_ - rest[1]) <= 3 else "FAIL", final=(w_, h_), memory=mem())
time.sleep(10)
c = cpu_window(30)
record("idle.cpu_after_endurance", "PASS" if c and c["mean"] < 0.2 else "FAIL", **(c or {}))
log_findings("after_endurance")

# ---------------------------------------------------------------- 8. tirer, détacher, raccrocher
phase("detachement")


def drag(points, hold=0.15, release=True):
    pyautogui.moveTo(*points[0])
    time.sleep(0.1)
    pyautogui.mouseDown()
    time.sleep(hold)
    for (x, y, d) in points[1:]:
        pyautogui.moveTo(x, y, duration=d)
    if release:
        pyautogui.mouseUp()


cases = {
    "pull_small_springback": [(cx, cy), (cx, cy + 6, 0.2), (cx, cy + 10, 0.2)],
    "pull_open_threshold": [(cx, cy), (cx, cy + 10, 0.2), (cx, cy + 22, 0.3)],
    "pull_tear_slow_down": [(cx, cy), (cx, cy + 20, 0.3), (cx, cy + 320, 1.2)],
}
for name, pts in cases.items():
    park(); time.sleep(2)
    s = watch(4.0, f"drag-{name}", action=lambda p=pts: drag(p))
    bbox = full_shape_bbox()
    shot(f"drag-{name}")
    record(f"drag.{name}", "INFO", **analyse(s, rest), full_bbox=bbox, windows=visible_windows())
    # Retour au bord : double-clic sur la notch flottante, sinon Échap.
    if bbox and bbox[1] > 20:
        pyautogui.doubleClick((bbox[0] + bbox[2]) // 2, (bbox[1] + bbox[3]) // 2)
        time.sleep(2.5)
        b2 = full_shape_bbox()
        record(f"drag.{name}.double_click_returns", "PASS" if b2 and b2[1] <= 2 else "FAIL", bbox_after=b2)
    pyautogui.press("escape")
    time.sleep(1)

# Lancer rapide vers un coin, puis vers la gauche et la droite.
for name, (tx, ty) in {"fling_corner_bottom_right": (SW - 40, SH - 120), "fling_left": (40, SH // 2),
                       "fling_right": (SW - 40, SH // 3), "drag_diagonal_top_left": (60, 60)}.items():
    park(); time.sleep(2)
    w_, h_, box = shape()
    sx, sy = notch_center(box)
    drag([(sx, sy), (sx, sy + 30, 0.15), (tx, ty, 0.25)], hold=0.1)
    time.sleep(3)
    bbox = full_shape_bbox()
    shot(f"{name}")
    record(f"drag.{name}", "INFO", full_bbox=bbox, windows=visible_windows())
    if bbox and bbox[1] > 20:
        pyautogui.doubleClick((bbox[0] + bbox[2]) // 2, (bbox[1] + bbox[3]) // 2)
        time.sleep(3)
    b2 = full_shape_bbox()
    record(f"drag.{name}.back_to_top", "PASS" if b2 and b2[1] <= 2 and abs((b2[0] + b2[2]) // 2 - SW // 2) < 30 else "FAIL",
           bbox_after=b2)
    if not (b2 and b2[1] <= 2):
        # Raccrochage forcé, pour la suite : relance.
        kill_app(); launch(); park(); time.sleep(5)

# Prise, traction, Échap pendant le glisser (annulation).
park(); time.sleep(2)
pyautogui.moveTo(cx, cy); pyautogui.mouseDown(); time.sleep(0.1)
pyautogui.moveTo(cx, cy + 120, duration=0.4)
pyautogui.press("escape")
time.sleep(0.3)
pyautogui.mouseUp()
time.sleep(3)
record("drag.escape_while_dragging", "INFO", full_bbox=full_shape_bbox())
shot("drag-echap")
b = full_shape_bbox()
if b and b[1] > 20:
    pyautogui.doubleClick((b[0] + b[2]) // 2, (b[1] + b[3]) // 2); time.sleep(3)

# ---------------------------------------------------------------- 9. plein écran
phase("plein-ecran")
park(); time.sleep(2)
fs_script = ("import tkinter as tk\nr=tk.Tk()\nr.attributes('-fullscreen',True)\nr.attributes('-topmost',True)\n"
             "r.configure(bg='#3060a0')\nr.after(25000,r.destroy)\nr.mainloop()\n")
fs = subprocess.Popen([sys.executable, "-c", fs_script])
time.sleep(4)
vis = visible_windows()
shot("plein-ecran")
record("fullscreen.withdraws", "PASS" if not vis else "FAIL", visible_windows=vis)
cli("--notify", "--title", "Pendant le plein écran", "--body", "ne doit pas apparaître")
time.sleep(3)
vis2 = visible_windows()
shot("plein-ecran-notification")
record("fullscreen.activity_stays_hidden", "PASS" if not vis2 else "FAIL", visible_windows=vis2)
fs.wait(40)
time.sleep(3)
vis3 = visible_windows()
shot("apres-plein-ecran")
record("fullscreen.returns", "PASS" if vis3 else "FAIL", visible_windows=vis3)
time.sleep(10)

# ---------------------------------------------------------------- 10. thème, animations réduites, contraste
phase("accessibilite")
park(); time.sleep(2)
# Animations réduites.
user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(0), 3)
time.sleep(2)
s1 = watch(2.5, "reduced-motion-open", action=click_open)
s2 = watch(2.5, "reduced-motion-close", action=lambda: pyautogui.press("escape"))
a1, a2 = analyse(s1, rest), analyse(s2, rest)
record("reduced_motion.open", "INFO", **a1, normal_open=a_open)
record("reduced_motion.close", "INFO", **a2, normal_close=a_close)
user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(1), 3)
park(); time.sleep(2)

# Thème clair de Windows.
for v in ("0", "1"):
    for name in ("AppsUseLightTheme", "SystemUsesLightTheme"):
        subprocess.run(["reg", "add", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "/v", name, "/t", "REG_DWORD", "/d", "1" if v == "0" else "0", "/f"], capture_output=True)
    win32gui.SendMessageTimeout(win32con.HWND_BROADCAST, win32con.WM_SETTINGCHANGE, 0, "ImmersiveColorSet",
                                win32con.SMTO_ABORTIFHUNG, 3000)
    time.sleep(2)
    click_open(); time.sleep(1.5)
    shot(f"theme-{'clair' if v == '0' else 'sombre'}-ouverte")
    pyautogui.press("escape"); park(); time.sleep(2)
    record(f"theme.{'light' if v == '0' else 'dark'}", "INFO", alive=APP.is_running())


# Contraste élevé.
class HIGHCONTRAST(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.UINT), ("dwFlags", wintypes.DWORD), ("lpszDefaultScheme", wintypes.LPWSTR)]


hc = HIGHCONTRAST(ctypes.sizeof(HIGHCONTRAST), 0x00000001, None)
ok = user32.SystemParametersInfoW(0x0043, ctypes.sizeof(hc), ctypes.byref(hc), 3)
time.sleep(5)
shot("contraste-eleve-repos")
click_open(); time.sleep(1.5)
shot("contraste-eleve-ouverte")
pyautogui.press("escape"); park()
hc.dwFlags = 0
user32.SystemParametersInfoW(0x0043, ctypes.sizeof(hc), ctypes.byref(hc), 3)
time.sleep(4)
record("high_contrast.toggle", "INFO", applied=bool(ok), alive=APP.is_running())
# Le fond a pu changer : on le remet.
elements = (ctypes.c_int * 1)(1)
colors = (wintypes.DWORD * 1)(BG[0] | (BG[1] << 8) | (BG[2] << 16))
user32.SetSysColors(1, elements, colors)
time.sleep(2)
rest_now, _ = rest_shape()
record("high_contrast.rest_shape_after", "PASS" if abs(rest_now[1] - rest[1]) <= 3 else "FAIL", rest=rest, now=rest_now)

# ---------------------------------------------------------------- 11. écran : résolution, échelle, Explorer
phase("ecran")


def set_res(w, h):
    dm = win32api.EnumDisplaySettings(None, win32con.ENUM_CURRENT_SETTINGS)
    dm.PelsWidth, dm.PelsHeight = w, h
    dm.Fields = win32con.DM_PELSWIDTH | win32con.DM_PELSHEIGHT
    return win32api.ChangeDisplaySettings(dm, 0)


r = set_res(1280, 720)
time.sleep(5)
SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
b = full_shape_bbox()
shot("resolution-1280")
record("display.resolution_change", "PASS" if b and abs((b[0] + b[2]) // 2 - SW // 2) < 30 and b[1] <= 2 else "FAIL",
       result=r, screen=(SW, SH), bbox=b, windows=visible_windows())
# Ouverte pendant le changement de résolution.
cx2 = SW // 2
pyautogui.moveTo(cx2, 6); pyautogui.click(); time.sleep(0.3)
r = set_res(1920, 1080)
time.sleep(5)
SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
pyautogui.press("escape"); park(); time.sleep(3)
b = full_shape_bbox()
shot("resolution-retour")
record("display.resolution_change_while_open", "PASS" if b and abs((b[0] + b[2]) // 2 - SW // 2) < 30 and b[1] <= 2 else "FAIL",
       result=r, bbox=b)
# Échelle (DPI) changée à chaud.
dpi_ok = user32.SystemParametersInfoW(0x009F, 2, None, 1)
time.sleep(6)
b = full_shape_bbox()
shot("echelle-plus")
record("display.dpi_change", "INFO", applied=bool(dpi_ok), system_dpi=user32.GetDpiForSystem(), bbox=b,
       windows=visible_windows())
click_open(); time.sleep(1.5); shot("echelle-plus-ouverte"); pyautogui.press("escape"); park()
user32.SystemParametersInfoW(0x009F, 0, None, 1)
time.sleep(6)
b = full_shape_bbox()
record("display.dpi_restored", "INFO", bbox=b)
rest2, rest_box2 = rest_shape()
cx, cy = notch_center(rest_box2)
# Explorer redémarré.
subprocess.run(["taskkill", "/F", "/IM", "explorer.exe"], capture_output=True)
time.sleep(2)
subprocess.Popen(["explorer.exe"])
time.sleep(8)
record("shell.explorer_restart", "PASS" if APP.is_running() and visible_windows() else "FAIL",
       alive=APP.is_running(), windows=visible_windows())
shot("apres-explorer")

# ---------------------------------------------------------------- 12. cycle de vie
phase("cycle-de-vie")
first_pid = APP.pid
t = time.monotonic()
second = subprocess.Popen([EXE])
try:
    second.wait(20)
    record("lifecycle.second_instance_exits", "PASS", seconds=round(time.monotonic() - t, 2),
           first_alive=psutil.pid_exists(first_pid))
except subprocess.TimeoutExpired:
    record("lifecycle.second_instance_exits", "FAIL", note="seconde instance toujours vivante après 20 s")
    second.kill()
time.sleep(2)
shot("apres-seconde-instance")
pyautogui.press("escape"); park()
save_log("log-03-session-principale.txt")
log_findings("main_session")
t_quit = quit_gracefully()
record("lifecycle.graceful_quit_after_session", "PASS" if t_quit else "FAIL", seconds=t_quit,
       shadow=[l for l in read_log().splitlines() if "OMBRE" in l][-5:])
save_log("log-04-arret-session.txt")
kill_app()

# Configuration corrompue.
shutil.copy(CONFIG, CONFIG + ".audit-backup")
with open(CONFIG, "w", encoding="utf-8") as f:
    f.write("{ \"WelcomeCompleted\": true, \"HoverToPreview\": ")
proc, ready = launch()
time.sleep(4)
record("resilience.corrupt_config", "PASS" if ready else "FAIL", ready=ready,
       log=[l for l in read_log().splitlines() if "CONFIG" in l or "config" in l][:10],
       files=os.listdir(os.path.dirname(CONFIG)))
shot("config-corrompue")
kill_app()
# Valeurs absurdes.
shutil.copy(CONFIG + ".audit-backup", CONFIG)
edit_config(SpringResponseSeconds=-5, Density="Galaxy", TearDistance=1e9, UpdateMode="Nope", CutoutWidth=-1,
            HorizontalOffset=99999)
proc, ready = launch()
time.sleep(4)
b = full_shape_bbox()
shot("config-absurde")
record("resilience.absurd_values", "PASS" if ready and b else "FAIL", ready=ready, bbox=b)
kill_app()
# Configuration en lecture seule.
shutil.copy(CONFIG + ".audit-backup", CONFIG)
os.chmod(CONFIG, 0o444)
proc, ready = launch()
time.sleep(3)
record("resilience.read_only_config", "PASS" if ready else "FAIL", ready=ready,
       log=[l for l in read_log().splitlines() if "CONFIG" in l][:5])
kill_app()
os.chmod(CONFIG, 0o666)

# ---------------------------------------------------------------- 13. greffons
phase("greffons")
os.makedirs(PLUGINS, exist_ok=True)
with open(os.path.join(PLUGINS, "Garbage.Plugin.dll"), "wb") as f:
    f.write(os.urandom(4096))
if TEST_PLUGIN and os.path.exists(TEST_PLUGIN):
    shutil.copy(TEST_PLUGIN, PLUGINS)
proc, ready = launch()
time.sleep(3)
record("plugins.unapproved", "PASS" if ready else "FAIL", ready=ready,
       log=[l for l in read_log().splitlines() if "PLUGIN" in l or "Greffon" in l][:10])
kill_app()
# Approuvé par empreinte, mais non signé : la version Release doit refuser.
if TEST_PLUGIN and os.path.exists(TEST_PLUGIN):
    import hashlib
    name = os.path.basename(TEST_PLUGIN)
    digest = hashlib.sha256(open(os.path.join(PLUGINS, name), "rb").read()).hexdigest().upper()
    garbage = hashlib.sha256(open(os.path.join(PLUGINS, "Garbage.Plugin.dll"), "rb").read()).hexdigest().upper()
    edit_config(ApprovedPlugins={name: digest, "Garbage.Plugin.dll": garbage})
    proc, ready = launch()
    time.sleep(3)
    record("plugins.approved_unsigned", "PASS" if ready else "FAIL", ready=ready,
           log=[l for l in read_log().splitlines() if "PLUGIN" in l or "Greffon" in l or "greffon" in l][:12])
    kill_app()
shutil.rmtree(PLUGINS, ignore_errors=True)
edit_config(ApprovedPlugins={})

# ---------------------------------------------------------------- 14. Pixel (yeux) au repos
phase("repos-avec-pixel")
edit_config(ShowPixel=True)
proc, ready = launch()
park()
time.sleep(10)
shot("pixel-repos")
c = cpu_window(90)
record("idle.cpu_with_pixel_eyes", "PASS" if c and c["mean"] < 0.5 else "FAIL", **(c or {}))
# Assoupissement (30 s sans saisie), puis réveil.
time.sleep(25)
shot("pixel-assoupi")
s = watch(4.0, "pixel-wake", action=lambda: pyautogui.moveTo(SW // 2 - 300, SH // 2, duration=0.3))
shot("pixel-reveil")
record("pixel.wake_from_doze", "INFO", **analyse(s, None))
time.sleep(5)
shot("pixel-apres-reveil")
log_findings("pixel")
save_log("log-05-pixel.txt")
kill_app()

# ---------------------------------------------------------------- 15. démonstration intégrée
phase("demo")
proc, ready = launch("--demo")
mark = time.monotonic()
demo_series = []
i = 0
while time.monotonic() - mark < 75:
    w_, h_, _ = shape()
    demo_series.append((round(time.monotonic() - mark, 2), w_, h_))
    if i % 40 == 0:
        shot(f"demo-{int(time.monotonic() - mark):02d}s")
    i += 1
    time.sleep(0.05)
with open(os.path.join(OUT, "series-demo.json"), "w") as f:
    json.dump(demo_series, f)
time.sleep(20)
c = cpu_window(40)
record("demo.cpu_after", "PASS" if c and c["mean"] < 0.3 else "FAIL", **(c or {}))
rest_d, _ = rest_shape()
record("demo.returns_to_rest", "INFO", final=rest_d, rest=rest)
log_findings("demo")
save_log("log-06-demo.txt")

# ---------------------------------------------------------------- 16. réseau (vérification de mise à jour à 2 min)
phase("reseau")
time.sleep(max(0, 135 - (time.monotonic() - mark)))
record("privacy.remote_endpoints_seen", "INFO", endpoints=sorted(seen_remote),
       update_log=[l for l in read_log().splitlines() if "MISE À JOUR" in l][:5])
save_log("log-07-reseau.txt")
kill_app()

phase("fin")
with open(os.path.join(OUT, "summary.json"), "w", encoding="utf-8") as f:
    json.dump(results, f, ensure_ascii=False, indent=1)
say("terminé")
