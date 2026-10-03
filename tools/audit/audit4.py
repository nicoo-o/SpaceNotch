"""Quatrième passage : yeux de Pixel avec une vraie saisie ; greffons signés (préparés par le workflow).

Usage : python audit4.py <SpaceNotch.exe> <sortie> <dossier des greffons signés>
"""
import ctypes, hashlib, json, os, shutil, subprocess, sys, time
from ctypes import wintypes
import mss, psutil, pyautogui, win32gui, win32process
from PIL import Image

EXE, OUT, SIGNED = map(os.path.abspath, sys.argv[1:4])
os.makedirs(OUT, exist_ok=True)
user32 = ctypes.windll.user32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
pyautogui.FAILSAFE = False
LOCAL, ROAMING = os.environ["LOCALAPPDATA"], os.environ["APPDATA"]
LOG = os.path.join(LOCAL, "SpaceNotch", "logs", "spacenotch.log")
CONFIG = os.path.join(ROAMING, "SpaceNotch", "config.json")
PLUGINS = os.path.join(LOCAL, "SpaceNotch", "plugins")
T0 = time.monotonic()


def record(test, status, **d):
    e = {"t": round(time.monotonic() - T0, 1), "test": test, "status": status, "details": d}
    open(os.path.join(OUT, "results4.jsonl"), "a", encoding="utf-8").write(json.dumps(e, ensure_ascii=False) + "\n")
    print(json.dumps(e, ensure_ascii=False)[:600], flush=True)


class LASTINPUTINFO(ctypes.Structure):
    _fields_ = [("cbSize", wintypes.UINT), ("dwTime", wintypes.DWORD)]


def idle_seconds():
    li = LASTINPUTINFO(ctypes.sizeof(LASTINPUTINFO), 0)
    user32.GetLastInputInfo(ctypes.byref(li))
    return (ctypes.windll.kernel32.GetTickCount() - li.dwTime) / 1000


def read_log():
    try:
        return open(LOG, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def launch(**cfg):
    subprocess.run(["taskkill", "/F", "/IM", "SpaceNotch.exe"], capture_output=True)
    time.sleep(1.5)
    base = dict(WelcomeCompleted=True, DockEdge="Top", UpdateMode="Off", ShowPixel=False, ApprovedPlugins={})
    base.update(cfg)
    os.makedirs(os.path.dirname(CONFIG), exist_ok=True)
    json.dump(base, open(CONFIG, "w", encoding="utf-8"))
    if os.path.exists(LOG):
        os.remove(LOG)
    p = subprocess.Popen([EXE])
    for _ in range(600):
        if "IslandWindow prête" in read_log():
            break
        time.sleep(0.05)
    return psutil.Process(p.pid)


SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
grab = mss.mss()


def crop(name):
    img = grab.grab({"left": SW // 2 - 200, "top": 0, "width": 400, "height": 80})
    Image.frombytes("RGB", img.size, img.rgb).save(os.path.join(OUT, f"4-{name}.png"))


# ---------------- Pixel : vraie saisie
SKIP_PIXEL = os.environ.get("AUDIT_SKIP_PIXEL") == "1"
pyautogui.press("shift")
app = launch(ShowPixel=True) if not SKIP_PIXEL else None
time.sleep(1)
pyautogui.press("shift")
time.sleep(3)
crop("pixel-repos-saisie-recente")
record("pixel.rest_after_real_input", "INFO", idle_s=idle_seconds())
for cycle in range(0 if SKIP_PIXEL else 3):
    time.sleep(36)
    crop(f"pixel-assoupi-{cycle}")
    idle_before = idle_seconds()
    pyautogui.press("shift")
    time.sleep(3)
    crop(f"pixel-reveil-{cycle}")
    record(f"pixel.cycle_{cycle}", "INFO", idle_before_wake=idle_before, idle_after=idle_seconds(),
           notes=[l for l in read_log().splitlines() if "REPOS" in l or "ANIMATION" in l][:5])
time.sleep(2)
if not SKIP_PIXEL:
    shutil.copy(LOG, os.path.join(OUT, "log-pixel.txt"))

# ---------------- greffons signés
for name in sorted(os.listdir(SIGNED)):
    src = os.path.join(SIGNED, name)
    if not name.endswith(".dll") or (os.environ.get("AUDIT_ONLY") and os.environ["AUDIT_ONLY"] not in name):
        continue
    subprocess.run(["taskkill", "/F", "/IM", "SpaceNotch.exe"], capture_output=True)
    time.sleep(1.5)
    shutil.rmtree(PLUGINS, ignore_errors=True)
    os.makedirs(PLUGINS, exist_ok=True)
    dst = os.path.join(PLUGINS, "SpaceNotch.AuditProbe.dll")
    shutil.copy(src, dst)
    digest = hashlib.sha256(open(dst, "rb").read()).hexdigest().upper()
    t = time.monotonic()
    app = launch(ApprovedPlugins={"SpaceNotch.AuditProbe.dll": digest})
    ready = round(time.monotonic() - t, 2)
    hung = probes = 0
    end = time.monotonic() + 12
    while time.monotonic() < end:
        def cb(h, acc):
            if win32process.GetWindowThreadProcessId(h)[1] == app.pid and win32gui.IsWindowVisible(h):
                acc.append(h)
            return True
        hs = []
        win32gui.EnumWindows(cb, hs)
        if hs:
            r = wintypes.DWORD()
            probes += 1
            hung += 0 if user32.SendMessageTimeoutW(hs[0], 0, 0, 0, 2, 250, ctypes.byref(r)) else 1
        time.sleep(0.1)
    log = read_log()
    loaded = "Greffons chargés : 2" in log
    record(f"plugin.{name}", "FAIL" if loaded else "PASS", loaded=loaded, ready_s=ready, ui_probes=probes, ui_hung=hung,
           windows_status=open(src + ".status", encoding="utf-8", errors="replace").read().strip() if os.path.exists(src + ".status") else "?",
           log=[l for l in log.splitlines() if "Greffon" in l or "PLUGIN" in l or "prête" in l][:6])
    if loaded:
        subprocess.run([EXE, "--progress", "--id", "victim", "--title", "Victime", "--percent", "50"], capture_output=True)
        time.sleep(48)
        crop(f"{name}-apres-effacement")
subprocess.run(["taskkill", "/F", "/IM", "SpaceNotch.exe"], capture_output=True)
print("terminé")
