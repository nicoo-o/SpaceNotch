"""Second passage de l'audit runtime : greffons hostiles, signature, ouverture spontanée,
accessibilité. Comme audit.py : mesure seulement, ne corrige rien.

Usage : python audit2.py <SpaceNotch.exe> <dossier de sortie> <SpaceNotch.AuditProbe.dll>
"""
import ctypes
import csv
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
os.makedirs(os.path.join(OUT, "shots"), exist_ok=True)
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
    with open(os.path.join(OUT, "results2.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")
    print(f"{now():8.2f} [{PHASE}] {status:13} {test} {json.dumps(details, ensure_ascii=False)[:400]}", flush=True)


def phase(name):
    global PHASE
    PHASE = name
    print("=" * 20, name, flush=True)


def prepare_desktop():
    dm = win32api.EnumDisplaySettings(None, win32con.ENUM_CURRENT_SETTINGS)
    dm.PelsWidth, dm.PelsHeight = 1920, 1080
    dm.Fields = win32con.DM_PELSWIDTH | win32con.DM_PELSHEIGHT
    win32api.ChangeDisplaySettings(dm, 0)
    user32.SystemParametersInfoW(0x1043, 0, ctypes.c_void_p(1), 3)
    user32.SystemParametersInfoW(0x103F, 0, ctypes.c_void_p(1), 3)
    user32.SystemParametersInfoW(0x0014, 0, "", 3)
    user32.SetSysColors(1, (ctypes.c_int * 1)(1), (wintypes.DWORD * 1)(BG[0] | (BG[1] << 8) | (BG[2] << 16)))
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


def launch(*args):
    global APP
    if os.path.exists(LOG):
        try:
            os.remove(LOG)
        except OSError:
            pass
    t = time.monotonic()
    proc = subprocess.Popen([EXE, *args])
    APP = psutil.Process(proc.pid)
    return t


def wait_ready(t, timeout=90):
    while time.monotonic() - t < timeout:
        if "IslandWindow prête" in read_log():
            return round(time.monotonic() - t, 2)
        time.sleep(0.05)
    return None


def kill_app():
    global APP
    subprocess.run(["taskkill", "/F", "/IM", "SpaceNotch.exe"], capture_output=True)
    time.sleep(1.5)
    APP = None


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


def windows_of(pid):
    found = []

    def cb(hwnd, _):
        if win32process.GetWindowThreadProcessId(hwnd)[1] == pid and win32gui.IsWindowVisible(hwnd):
            found.append(hwnd)
        return True

    win32gui.EnumWindows(cb, None)
    return found


def responsive(hwnd, timeout_ms=250):
    result = wintypes.DWORD()
    ok = user32.SendMessageTimeoutW(hwnd, 0, 0, 0, 0x0002, timeout_ms, ctypes.byref(result))
    return bool(ok)


grab = mss.mss()
SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)


def shape():
    width = min(1400, SW)
    reg = {"left": max(0, SW // 2 - width // 2), "top": 0, "width": width, "height": 520}
    img = np.asarray(grab.grab(reg))[:, :, :3]
    dark = img.mean(axis=2) < 110
    top = dark[:6].any(axis=0)
    if not top.any():
        return 0, 0
    xs = np.where(top)[0]
    col = dark[:, (xs.min() + xs.max()) // 2]
    ys = np.where(col)[0]
    return int(xs.max() - xs.min() + 1), int(ys.max() + 1) if len(ys) else 0


def shot(name):
    d = os.path.join(OUT, "shots")
    img = grab.grab({"left": 0, "top": 0, "width": SW, "height": SH})
    Image.frombytes("RGB", img.size, img.rgb).save(os.path.join(d, f"2-{len(os.listdir(d)):03d}-{name}.png"))


def cli(*args, stdin=None, timeout=60):
    t = time.monotonic()
    p = subprocess.run([EXE, *args], input=stdin, capture_output=True, timeout=timeout)
    return p.returncode, round(time.monotonic() - t, 2), p.stdout.decode("utf-8", "replace")[:300]


def ps(cmd):
    return subprocess.run(["powershell", "-NoProfile", "-Command", cmd], capture_output=True, text=True).stdout.strip()


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest().upper()


# ==========================================================================
phase("environnement")
prepare_desktop()
SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
grab = mss.mss()
record("env.screen", "INFO", screen=(SW, SH))
kill_app()
shutil.rmtree(os.path.join(LOCAL, "SpaceNotch"), ignore_errors=True)
shutil.rmtree(os.path.dirname(CONFIG), ignore_errors=True)
edit_config(WelcomeCompleted=True)

# Référence : démarrage sans greffon, puis forme au repos.
pyautogui.moveTo(SW - 200, SH // 2)
t = launch()
base_ready = wait_ready(t)
time.sleep(6)
rest = shape()
record("baseline.ready", "INFO", seconds=base_ready, rest=rest)
kill_app()

# --------------------------------------------------------------------------
phase("signature-auto-signee")
os.makedirs(PLUGINS, exist_ok=True)
signed = os.path.join(PLUGINS, "SpaceNotch.AuditProbe.dll")
shutil.copy(PROBE, signed)
pfx = os.path.join(OUT, "attacker.pfx")
mk = subprocess.run(["powershell", "-NoProfile", "-Command",
    "$c = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Audit Attacker' -CertStoreLocation Cert:\\CurrentUser\\My;"
    f"$p = ConvertTo-SecureString -String 'audit' -Force -AsPlainText; Export-PfxCertificate -Cert $c -FilePath '{pfx}' -Password $p | Out-Null; 'ok'"],
    capture_output=True, text=True)
tools = sorted(__import__("glob").glob(r"C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe"))
signing = subprocess.run([tools[-1], "sign", "/fd", "SHA256", "/f", pfx, "/p", "audit", signed],
                         capture_output=True, text=True) if tools else None
sig = ps(f"(Get-AuthenticodeSignature '{signed}').Status")
record("plugin.self_signed.signing", "INFO", cert=mk.stdout.strip() + mk.stderr.strip()[:300],
       signtool=(signing.stdout + signing.stderr)[-400:] if signing else "signtool introuvable")
record("plugin.self_signed.status_seen_by_windows", "INFO", windows_status=sig)
edit_config(ApprovedPlugins={"SpaceNotch.AuditProbe.dll": sha(signed)})

t = launch()
hangs, probes = 0, 0
hang_series = []
while time.monotonic() - t < 25:
    if APP and APP.is_running():
        for h in windows_of(APP.pid):
            probes += 1
            ok = responsive(h)
            hangs += 0 if ok else 1
            hang_series.append((round(time.monotonic() - t, 2), ok))
            break
    if "IslandWindow prête" in read_log() and time.monotonic() - t > 12:
        break
    time.sleep(0.1)
ready = wait_ready(t, 5)
log = read_log()
loaded = [l for l in log.splitlines() if "Greffon" in l or "PLUGIN" in l or "plugin" in l]
record("plugin.self_signed_untrusted_loaded",
       "FAIL" if "Greffons chargés : 2" in log or "Sonde" in log else "PASS",
       note="FAIL = un greffon signé par un certificat non approuvé est chargé malgré l'exigence Authenticode",
       log=loaded[:8])
record("plugin.slow_start_freezes_ui", "FAIL" if hangs > 3 else "PASS",
       ready_seconds=ready, baseline_ready=base_ready, ui_probes=probes, ui_hung=hangs,
       first_hang_at=next((s[0] for s in hang_series if not s[1]), None),
       last_hang_at=next((s[0] for s in reversed(hang_series) if not s[1]), None))
shot("greffon-lent")

# Une activité d'une autre fonctionnalité, avant l'inondation.
cli("--progress", "--id", "victim", "--title", "Victime", "--percent", "50")
time.sleep(2)
spam_start = time.monotonic()
mem0 = APP.memory_info().rss / 2**20
APP.cpu_percent(None)
cpu = []
shapes = []
while time.monotonic() - spam_start < 40:
    time.sleep(1)
    try:
        cpu.append(APP.cpu_percent(None) / psutil.cpu_count())
        shapes.append(shape())
    except psutil.Error:
        break
    if int(time.monotonic() - spam_start) in (8, 16, 24):
        shot(f"inondation-{int(time.monotonic() - spam_start)}s")
record("plugin.spam_2000_activities", "INFO", alive=APP.is_running(), cpu_mean=round(float(np.mean(cpu)), 2),
       cpu_max=round(float(np.max(cpu)), 2), mem_before_mb=round(mem0, 1),
       mem_after_mb=round(APP.memory_info().rss / 2**20, 1), shapes=shapes[::4])
time.sleep(5)
shot("apres-inondation")
# La victime a-t-elle été effacée par le greffon ?
log = read_log()
record("plugin.can_remove_foreign_activities", "INFO", final_shape=shape(), rest=rest,
       note="si la forme est revenue au repos alors que la progression « Victime » n'a jamais été terminée, le greffon l'a effacée")
cli("--progress", "--id", "victim", "--clear")
kill_app()

# --------------------------------------------------------------------------
phase("signature-alteree")
data = bytearray(open(signed, "rb").read())
needle = "Sonde lente".encode("utf-16-le")
pos = data.find(needle)
if pos >= 0:
    data[pos] = ord("T")
    tampered = os.path.join(PLUGINS, "SpaceNotch.AuditProbe.dll")
    open(tampered, "wb").write(bytes(data))
    status = ps(f"(Get-AuthenticodeSignature '{tampered}').Status")
    edit_config(ApprovedPlugins={"SpaceNotch.AuditProbe.dll": sha(tampered)})
    t = launch()
    ready = wait_ready(t, 60)
    time.sleep(2)
    log = read_log()
    record("plugin.tampered_signature_loaded",
           "FAIL" if "Greffons chargés : 2" in log else "PASS",
           windows_status=status, ready=ready,
           log=[l for l in log.splitlines() if "Greffon" in l or "PLUGIN" in l][:6])
    kill_app()
else:
    record("plugin.tampered_signature_loaded", "NOT_MEASURED", reason="chaîne introuvable dans l'assemblage")
shutil.rmtree(PLUGINS, ignore_errors=True)
edit_config(ApprovedPlugins={})

# --------------------------------------------------------------------------
phase("ouverture-spontanee")
t = launch()
wait_ready(t)
time.sleep(4)
pyautogui.moveTo(SW - 200, SH // 2)
rest = shape()
cli("--progress", "--id", "boom", "--title", "Build", "--label", "échec", "--error")
time.sleep(2.5)
opened = shape()
shot("erreur-ouverte-seule")
fg = win32gui.GetForegroundWindow()
fg_name = psutil.Process(win32process.GetWindowThreadProcessId(fg)[1]).name() if fg else "?"
record("auto_open.high_priority_opens", "INFO", rest=rest, opened=opened, foreground=fg_name)
pyautogui.click(SW // 2 + 500, SH // 2 + 100)
time.sleep(2.5)
after_click = shape()
record("auto_open.click_outside_closes", "PASS" if abs(after_click[1] - rest[1]) <= 3 else "FAIL",
       after=after_click, rest=rest, opened=opened)
pyautogui.press("escape")
time.sleep(2)
after_esc = shape()
record("auto_open.escape_closes", "PASS" if abs(after_esc[1] - rest[1]) <= 3 else "FAIL", after=after_esc)
shot("erreur-apres-echap")
t_wait = time.monotonic()
while time.monotonic() - t_wait < 120 and abs(shape()[1] - rest[1]) > 3:
    time.sleep(1)
record("auto_open.error_card_lifetime", "INFO", seconds=round(time.monotonic() - t_wait, 1),
       closed=abs(shape()[1] - rest[1]) <= 3)
cli("--progress", "--id", "boom", "--clear")
time.sleep(2)

# Demande d'autorisation d'un agent (hook) : prioritaire, bloquante.
hook = json.dumps({"hook_event_name": "PermissionRequest", "session_id": "audit", "cwd": "C:\\\\projet",
                   "tool_name": "Bash", "tool_input": {"command": "rm -rf build"}}).encode()
t_hook = time.monotonic()
h = subprocess.Popen([EXE, "--hook"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
h.stdin.write(hook)
h.stdin.close()
time.sleep(3)
shot("agent-demande")
record("agent.permission_request_shape", "INFO", shape=shape(), rest=rest)
try:
    h.wait(200)
    record("agent.permission_request_unanswered_returns", "INFO", seconds_waited=round(time.monotonic() - t_hook, 1),
           exit=h.returncode, stdout=h.stdout.read().decode("utf-8", "replace")[:200])
except subprocess.TimeoutExpired:
    record("agent.permission_request_unanswered_returns", "FAIL", note="le hook bloque l'agent plus de 200 s")
    h.kill()
time.sleep(3)
record("agent.after_timeout_shape", "INFO", shape=shape(), rest=rest)
shot("agent-apres-delai")

# --------------------------------------------------------------------------
phase("accessibilite-uia")
try:
    import uiautomation as auto

    cli("--notify", "--title", "Lecteur d'écran", "--body", "Annonce de test")
    time.sleep(1.5)
    names = []
    for hwnd in windows_of(APP.pid):
        ctl = auto.ControlFromHandle(hwnd)
        for c, depth in auto.WalkControl(ctl, maxDepth=6):
            if c.Name:
                names.append((depth, c.ControlTypeName, c.Name[:80], c.IsKeyboardFocusable))
    record("uia.tree", "INFO", count=len(names), sample=names[:80])
except Exception as ex:  # noqa: BLE001
    record("uia.tree", "NOT_MEASURED", reason=str(ex))

# --------------------------------------------------------------------------
phase("fin")
shutil.copy(LOG, os.path.join(OUT, "log-audit2.txt")) if os.path.exists(LOG) else None
kill_app()
print("terminé", flush=True)
