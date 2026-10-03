"""Cinquième passage : diagnostic du clic à travers le halo (SN-02) et retour de la languette (SN-08).

Usage : python audit5.py <SpaceNotch.exe> <sortie>
"""
import ctypes, json, os, subprocess, sys, time
import mss, psutil, pyautogui, win32con, win32gui, win32process
from PIL import Image

EXE, OUT = map(os.path.abspath, sys.argv[1:3])
os.makedirs(OUT, exist_ok=True)
user32 = ctypes.windll.user32
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
pyautogui.FAILSAFE = False
pyautogui.PAUSE = 0.02
LOCAL, ROAMING = os.environ["LOCALAPPDATA"], os.environ["APPDATA"]
LOG = os.path.join(LOCAL, "SpaceNotch", "logs", "spacenotch.log")
CONFIG = os.path.join(ROAMING, "SpaceNotch", "config.json")
T0 = time.monotonic()
APP = None
SW, SH = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
grab = mss.mss()


def record(test, status, **d):
    e = {"t": round(time.monotonic() - T0, 1), "test": test, "status": status, "details": d}
    with open(os.path.join(OUT, "results5.jsonl"), "a", encoding="utf-8") as f:
        f.write(json.dumps(e, ensure_ascii=False, default=str) + "\n")
    print(json.dumps(e, ensure_ascii=False, default=str)[:800], flush=True)


def read_log():
    try:
        return open(LOG, encoding="utf-8", errors="replace").read()
    except OSError:
        return ""


def launch(**cfg):
    global APP
    subprocess.run(["taskkill", "/F", "/IM", "SpaceNotch.exe"], capture_output=True)
    time.sleep(1.5)
    base = dict(WelcomeCompleted=True, DockEdge="Top", UpdateMode="Off", ShowPixel=False, PixelDefaultApplied=True,
                ApprovedPlugins={}, Appearance="Dark")
    base.update(cfg)
    os.makedirs(os.path.dirname(CONFIG), exist_ok=True)
    json.dump(base, open(CONFIG, "w", encoding="utf-8"))
    if os.path.exists(LOG):
        os.remove(LOG)
    p = subprocess.Popen([EXE])
    APP = psutil.Process(p.pid)
    for _ in range(600):
        if "IslandWindow prête" in read_log():
            break
        time.sleep(0.05)
    time.sleep(2)


def proc_of(hwnd):
    try:
        return psutil.Process(win32process.GetWindowThreadProcessId(hwnd)[1]).name()
    except Exception:
        return "?"


def describe(hwnd):
    if not hwnd:
        return None
    return {"hwnd": hwnd, "class": win32gui.GetClassName(hwnd), "title": win32gui.GetWindowText(hwnd),
            "proc": proc_of(hwnd), "rect": win32gui.GetWindowRect(hwnd),
            "style": hex(win32gui.GetWindowLong(hwnd, win32con.GWL_STYLE) & 0xFFFFFFFF),
            "exstyle": hex(win32gui.GetWindowLong(hwnd, win32con.GWL_EXSTYLE) & 0xFFFFFFFF),
            "enabled": bool(win32gui.IsWindowEnabled(hwnd)), "visible": bool(win32gui.IsWindowVisible(hwnd))}


def windows_of(pid, visible=True):
    found = []

    def cb(hwnd, _):
        if win32process.GetWindowThreadProcessId(hwnd)[1] == pid and (not visible or win32gui.IsWindowVisible(hwnd)):
            found.append(hwnd)
        return True

    win32gui.EnumWindows(cb, None)
    return found


def children(hwnd):
    found = []
    try:
        win32gui.EnumChildWindows(hwnd, lambda h, _: found.append(h) or True, None)
    except Exception:
        pass
    return found


def island():
    for h in windows_of(APP.pid):
        if win32gui.GetWindowText(h) == "SpaceNotch":
            return h
    return None


def atmosphere():
    for h in windows_of(APP.pid, visible=False):
        if "Atmosphere" in win32gui.GetWindowText(h):
            return h
    return None


def foreground():
    return proc_of(win32gui.GetForegroundWindow())


def notepad():
    subprocess.run(["taskkill", "/F", "/IM", "notepad.exe"], capture_output=True)
    subprocess.Popen(["notepad.exe"])
    time.sleep(2.5)
    hwnd = win32gui.FindWindow("Notepad", None)
    try:
        win32gui.ShowWindow(hwnd, win32con.SW_MAXIMIZE)
        win32gui.SetForegroundWindow(hwnd)
    except Exception:
        pass
    time.sleep(1)
    return hwnd


def park():
    pyautogui.moveTo(SW // 2, SH // 2 + 200)


def crop(name, box):
    img = grab.grab({"left": box[0], "top": box[1], "width": box[2] - box[0], "height": box[3] - box[1]})
    Image.frombytes("RGB", img.size, img.rgb).save(os.path.join(OUT, f"5-{name}.png"))


def click_through(label, x, y):
    """Le curseur part de loin (aucun survol avant), puis clic réel : Notepad doit passer devant."""
    park()
    time.sleep(1.5)
    hit = win32gui.WindowFromPoint((x, y))
    root = user32.GetAncestor(hit, 2) if hit else 0
    try:
        win32gui.SetForegroundWindow(win32gui.FindWindow("Shell_TrayWnd", None))
    except Exception:
        pass
    time.sleep(0.4)
    pyautogui.click(x, y)
    time.sleep(0.6)
    fg = foreground()
    record(f"clickthrough.{label}", "PASS" if fg.lower() == "notepad.exe" else "FAIL",
           point=(x, y), hit=describe(hit), root=describe(root), foreground_after_click=fg,
           island=describe(island()))
    pyautogui.press("escape")
    time.sleep(0.6)


# --------------------------------------------------------------------------
# SN-02 : qui prend le clic à côté de la notch ?
launch()
park()
time.sleep(2)
isl, atm = island(), atmosphere()
record("layout", "INFO", island=describe(isl), island_children=[describe(c) for c in children(isl)],
       atmosphere=describe(atm), atmosphere_children=[describe(c) for c in children(atm)] if atm else None)
notepad()
ir = win32gui.GetWindowRect(isl)
points = {"droite_y8": (ir[2] + 30, 8), "gauche_y8": (ir[0] - 30, 8), "droite_y40": (ir[2] + 30, 40),
          "sous_y38": ((ir[0] + ir[2]) // 2, ir[3] + 20)}
crop("halo-repos", (SW // 2 - 200, 0, SW // 2 + 200, 100))
for name, (x, y) in points.items():
    click_through(f"a.{name}", x, y)

# Expérience B : halo masqué.
atm = atmosphere()
if atm:
    win32gui.ShowWindow(atm, win32con.SW_HIDE)
    time.sleep(0.5)
    for name in ("droite_y8", "gauche_y8"):
        click_through(f"b.halo_masque.{name}", *points[name])
    win32gui.ShowWindow(atm, win32con.SW_SHOWNOACTIVATE)
    time.sleep(0.5)

# Expérience C : enfants du halo désactivés.
atm = atmosphere()
if atm:
    for c in children(atm):
        win32gui.EnableWindow(c, False)
    time.sleep(0.3)
    for name in ("droite_y8", "gauche_y8"):
        click_through(f"c.enfants_desactives.{name}", *points[name])
    win32gui.EnableWindow(atm, False)
    time.sleep(0.3)
    for name in ("droite_y8",):
        click_through(f"c2.halo_desactive.{name}", *points[name])
    crop("halo-desactive", (SW // 2 - 200, 0, SW // 2 + 200, 100))

# Expérience D : enfants du halo superposés et transparents.
launch()
park()
time.sleep(2)
notepad()
atm = atmosphere()
if atm:
    for c in children(atm):
        ex = win32gui.GetWindowLong(c, win32con.GWL_EXSTYLE)
        win32gui.SetWindowLong(c, win32con.GWL_EXSTYLE, ex | win32con.WS_EX_LAYERED | win32con.WS_EX_TRANSPARENT)
    time.sleep(0.5)
    record("d.children_after", "INFO", children=[describe(c) for c in children(atm)])
    for name in ("droite_y8", "gauche_y8"):
        click_through(f"d.enfants_superposes.{name}", *points[name])
    crop("halo-enfants-superposes", (SW // 2 - 200, 0, SW // 2 + 200, 100))

# Expérience E : la notch elle-même masquée (le halo seul reste).
launch()
park()
time.sleep(2)
notepad()
isl = island()
if isl:
    win32gui.ShowWindow(isl, win32con.SW_HIDE)
    time.sleep(0.5)
    for name in ("droite_y8", "gauche_y8"):
        click_through(f"e.notch_masquee.{name}", *points[name])
subprocess.run(["taskkill", "/F", "/IM", "notepad.exe"], capture_output=True)

# --------------------------------------------------------------------------
# SN-08 : la pastille ramenée vite au centre haut s'y raccroche.
launch()
park()
time.sleep(1)
pyautogui.moveTo(SW // 2, 8)
pyautogui.mouseDown()
time.sleep(0.1)
pyautogui.moveTo(SW // 2, 40, duration=0.15)
pyautogui.moveTo(SW - 300, SH - 200, duration=0.25)
pyautogui.mouseUp()
time.sleep(3)
pill = describe(island())
record("tab.thrown", "INFO", island=pill)
r = pill["rect"]
pyautogui.moveTo((r[0] + r[2]) // 2, (r[1] + r[3]) // 2)
pyautogui.mouseDown()
time.sleep(0.1)
pyautogui.moveTo(r[0] - 40, (r[1] + r[3]) // 2, duration=0.2)
pyautogui.moveTo(SW // 2, 3, duration=0.4)
pyautogui.mouseUp()
time.sleep(3)
back = describe(island())
ok = back and back["rect"][1] == 0 and abs((back["rect"][0] + back["rect"][2]) / 2 - SW / 2) < 30
record("tab.fast_drag_back_to_top", "PASS" if ok else "FAIL", island=back)

# Languette latérale : double-clic, retour en haut.
launch()
park()
time.sleep(1)
pyautogui.moveTo(SW // 2, 8)
pyautogui.mouseDown()
time.sleep(0.1)
pyautogui.moveTo(SW // 2, 40, duration=0.15)
pyautogui.moveTo(SW - 3, SH // 2, duration=0.6)
time.sleep(0.3)
pyautogui.mouseUp()
time.sleep(3)
side = describe(island())
record("side.created", "INFO", island=side)
r = side["rect"]
pyautogui.doubleClick((r[0] + r[2]) // 2, (r[1] + r[3]) // 2)
time.sleep(3)
back = describe(island())
ok = back and back["rect"][1] == 0 and abs((back["rect"][0] + back["rect"][2]) / 2 - SW / 2) < 30
record("side.double_click_back_to_top", "PASS" if ok else "FAIL", island=back,
       log=[l for l in read_log().splitlines() if "raccroch" in l or "languette" in l][-5:])

# Rendu du halo avec l'attribut de superposition : capture de la notch ouverte.
launch()
park()
time.sleep(1)
pyautogui.click(SW // 2, 6)
time.sleep(2)
crop("ouverte-halo", (SW // 2 - 400, 0, SW // 2 + 400, 300))
record("render.open", "INFO", island=describe(island()), atmosphere=describe(atmosphere()))
pyautogui.press("escape")
time.sleep(1)

subprocess.run(["taskkill", "/F", "/IM", "SpaceNotch.exe"], capture_output=True)
