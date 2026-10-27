"""The desktop app: one window (Edge WebView2 through pywebview) showing the local server's UI, and a tray icon.

The window is frameless and the UI draws its own title bar. Moving, snapping and resizing are still Windows' own:
on mouse down the UI calls start_move / start_resize, which hand the drag to Windows (WM_NCLBUTTONDOWN with the
matching hit-test code, as Tauri does), so Aero Snap and the resize cursors work as usual.
Windows doesn't snap a borderless window, so the app does what matters: dropping the window with the cursor at the
top of a screen maximizes it, and dragging a maximized window's title bar restores it under the cursor.
Closing the window hides it to the tray and the music keeps playing; "Quit" in the tray menu really exits.
"""
import ctypes
import json
import os
import sys
import threading
import time
import urllib.request
from ctypes import wintypes

import webview

from . import __version__, config, server, settings

WM_NCLBUTTONDOWN = 0x00A1
GWL_STYLE, WS_MINIMIZEBOX, WS_SYSMENU = -16, 0x00020000, 0x00080000
VK_LBUTTON, VK_RBUTTON, SM_SWAPBUTTON = 0x01, 0x02, 23
SNAP_DISTANCE = 4                 # px from the top of the screen that counts as "dropped at the top"
DRAG_THRESHOLD = 6                # px the mouse must move before a maximized window comes off the top
HIT = {"move": 2, "left": 10, "right": 11, "top": 12, "top-left": 13, "top-right": 14,
       "bottom": 15, "bottom-left": 16, "bottom-right": 17}
DEFAULT_SIZE, MIN_SIZE = (1360, 860), (980, 640)


def system_dark():
    try:
        import winreg
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
        return winreg.QueryValueEx(key, "AppsUseLightTheme")[0] == 0
    except OSError:
        return False


class Bridge:
    """Called from the page as window.pywebview.api.<name>(). Runs on pywebview's worker threads."""

    def __init__(self, shell):
        self._shell = shell

    def minimize(self):
        self._shell.window.minimize()

    def toggle_maximize(self):
        self._shell.toggle_maximize()
        return self._shell.maximized

    def is_maximized(self):
        return self._shell.maximized

    def close(self):
        self._shell.hide()

    def quit(self):
        self._shell.quit()

    def start_move(self):
        self._shell.native_drag("move")

    def start_resize(self, edge):
        if edge in HIT:
            self._shell.native_drag(edge)

    def pick_library_file(self):
        paths = self._shell.window.create_file_dialog(webview.FileDialog.OPEN,
                                                      file_types=("MUMUsic library (*.mumu)", "All files (*.*)"))
        return paths[0] if paths else None

    def open_url(self, url):
        if url.startswith("https://"):
            os.startfile(url)


class Shell:
    def __init__(self, url):
        self.url = url
        self.maximized = False
        self.tray = None
        size = settings.get("window") or DEFAULT_SIZE
        self.window = webview.create_window(
            config.APP_NAME, url, js_api=Bridge(self), width=size[0], height=size[1], min_size=MIN_SIZE,
            frameless=True, easy_drag=False, shadow=True, text_select=True,
            background_color="#0c0d0e" if system_dark() else "#e8e8e3")
        self.window.events.closing += self.on_closing
        self.window.events.maximized += lambda: self.set_maximized(True)
        self.window.events.restored += lambda: self.set_maximized(False)
        self.window.events.shown += self.on_shown
        self.quitting = False

    # ---- native window details (WinForms form behind pywebview)

    def form(self):
        return self.window.native

    def on_ui(self, fn):
        from System import Action
        self.form().BeginInvoke(Action(fn))

    def on_shown(self):
        def setup():
            form = self.form()
            from System.Drawing import Icon
            form.Icon = Icon(str(config.ICON_ICO))
            hwnd = form.Handle.ToInt64()
            # Windows 11 rounded corners for a borderless window (DWMWA_WINDOW_CORNER_PREFERENCE = round)
            ctypes.windll.dwmapi.DwmSetWindowAttribute(ctypes.c_void_p(hwnd), 33, ctypes.byref(ctypes.c_int(2)), 4)
            self.allow_taskbar_minimize()
            self.fit_maximized_bounds()
        self.on_ui(setup)

    def allow_taskbar_minimize(self):
        """Clicking the taskbar button of the active window minimizes it only when the window has a minimize box,
        which a borderless form lacks. Both styles add nothing visible without a caption."""
        user32 = ctypes.windll.user32
        hwnd = ctypes.c_void_p(self.form().Handle.ToInt64())
        style = user32.GetWindowLongW(hwnd, GWL_STYLE)
        if style & (WS_MINIMIZEBOX | WS_SYSMENU) != WS_MINIMIZEBOX | WS_SYSMENU:
            user32.SetWindowLongW(hwnd, GWL_STYLE, style | WS_MINIMIZEBOX | WS_SYSMENU)

    def fit_maximized_bounds(self):
        """A maximized borderless form covers the taskbar unless told the work area (relative to its monitor)."""
        from System.Drawing import Rectangle
        from System.Windows.Forms import Screen
        form = self.form()
        screen = Screen.FromHandle(form.Handle)
        wa, b = screen.WorkingArea, screen.Bounds
        form.MaximizedBounds = Rectangle(wa.X - b.X, wa.Y - b.Y, wa.Width, wa.Height)

    def native_drag(self, kind):
        if kind == "move" and self.maximized:
            threading.Thread(target=self.drag_from_maximized, daemon=True).start()
            return
        self.on_ui(lambda: self.begin_drag(kind))
        if kind == "move":
            threading.Thread(target=self.snap_when_dropped, daemon=True).start()

    def begin_drag(self, kind):
        """UI thread: hand the drag to Windows' own move / resize loop."""
        self.fit_maximized_bounds()
        user32 = ctypes.windll.user32
        point = cursor()
        user32.ReleaseCapture()
        user32.PostMessageW(ctypes.c_void_p(self.form().Handle.ToInt64()), WM_NCLBUTTONDOWN, HIT[kind],
                            (point.y & 0xFFFF) << 16 | (point.x & 0xFFFF))

    def snap_when_dropped(self):
        time.sleep(0.1)
        while mouse_down():
            time.sleep(0.03)
        point = cursor()
        if point.y <= monitor_top(point) + SNAP_DISTANCE and not self.maximized:
            self.toggle_maximize()

    def drag_from_maximized(self):
        start = cursor()
        while mouse_down():
            point = cursor()
            if abs(point.x - start.x) > DRAG_THRESHOLD or abs(point.y - start.y) > DRAG_THRESHOLD:
                def restore_and_move():
                    from System.Windows.Forms import FormWindowState
                    form = self.form()
                    share = (start.x - form.Left) / max(1, form.Width)      # keep the grab point under the cursor
                    form.WindowState = FormWindowState.Normal
                    form.Left, form.Top = int(point.x - share * form.Width), max(0, point.y - 16)
                    self.begin_drag("move")
                self.on_ui(restore_and_move)
                self.snap_when_dropped()
                return
            time.sleep(0.015)

    def toggle_maximize(self):
        if self.maximized:
            self.window.restore()
        else:
            self.on_ui(self.fit_maximized_bounds)
            self.window.maximize()

    def set_maximized(self, value):
        self.maximized = value
        self.window.evaluate_js(f"window.onWindowState && onWindowState({json.dumps(value)})")

    # ---- tray, hide / show, quit

    def on_closing(self):
        """The window's own close (Alt+F4) hides to the tray too; only quit() really closes."""
        if self.quitting:
            return True
        self.hide()
        return False

    def hide(self):
        self.window.hide()

    def show(self):
        self.window.show()

        def activate():
            from System.Windows.Forms import FormWindowState
            form = self.form()
            if form.WindowState == FormWindowState.Minimized:
                form.WindowState = FormWindowState.Maximized if self.maximized else FormWindowState.Normal
            form.Activate()
        self.on_ui(activate)

    def quit(self):
        if not self.maximized:
            settings.update(window=[self.window.width, self.window.height])
        self.quitting = True
        if self.tray:
            self.tray.stop()
        config.INSTANCE_FILE.unlink(missing_ok=True)
        self.window.destroy()

    def player(self, call):
        self.window.evaluate_js(f"window.trayCommand && trayCommand({json.dumps(call)})")

    def start_tray(self):
        import pystray
        from PIL import Image
        menu = pystray.Menu(
            pystray.MenuItem("Show MUMUsic", lambda: self.show(), default=True),
            pystray.MenuItem("Play / pause", lambda: self.player("play")),
            pystray.MenuItem("Next song", lambda: self.player("next")),
            pystray.MenuItem("Previous song", lambda: self.player("prev")),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Quit", lambda: self.quit()),
        )
        self.tray = pystray.Icon(config.APP_NAME, Image.open(config.ICON_PNG), config.APP_NAME, menu)
        threading.Thread(target=self.tray.run, daemon=True).start()


def cursor():
    point = wintypes.POINT()
    ctypes.windll.user32.GetCursorPos(ctypes.byref(point))
    return point


def mouse_down():
    """Primary button held (the physical right one when the buttons are swapped)."""
    user32 = ctypes.windll.user32
    button = VK_RBUTTON if user32.GetSystemMetrics(SM_SWAPBUTTON) else VK_LBUTTON
    return bool(user32.GetAsyncKeyState(button) & 0x8000)


def monitor_top(point):
    """Top of the work area of the screen under the point."""
    class MonitorInfo(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.DWORD), ("rcMonitor", wintypes.RECT), ("rcWork", wintypes.RECT),
                    ("dwFlags", wintypes.DWORD)]
    user32 = ctypes.windll.user32
    user32.MonitorFromPoint.restype = ctypes.c_void_p
    monitor = user32.MonitorFromPoint(point, 2)          # MONITOR_DEFAULTTONEAREST
    info = MonitorInfo(cbSize=ctypes.sizeof(MonitorInfo))
    user32.GetMonitorInfoW(ctypes.c_void_p(monitor), ctypes.byref(info))
    return info.rcWork.top


def show_running_instance():
    """If the app already runs, ask it to show its window. True when it did."""
    try:
        port = json.loads(config.INSTANCE_FILE.read_text(encoding="utf-8"))["port"]
        req = urllib.request.Request(f"http://127.0.0.1:{port}/api/window/show", data=b"{}", method="POST")
        with urllib.request.urlopen(req, timeout=2) as r:
            return r.status == 200
    except (OSError, ValueError, KeyError):
        return False


def open_log():
    """The packaged app has no console: its messages and errors go to LOG_FILE, restarted when it gets big."""
    config.USER_DIR.mkdir(parents=True, exist_ok=True)
    big = config.LOG_FILE.exists() and config.LOG_FILE.stat().st_size > 1_000_000
    sys.stdout = sys.stderr = open(config.LOG_FILE, "w" if big else "a", encoding="utf-8", errors="replace",
                                   buffering=1)
    print(f"--- {config.APP_NAME} {__version__} started {time.strftime('%Y-%m-%d %H:%M:%S')}", flush=True)


def main():
    if config.FROZEN and "--uninstall" not in sys.argv:
        open_log()
    elif sys.stdout:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if "--uninstall" in sys.argv:
        from . import install
        return install.uninstall()
    if show_running_instance():
        return
    if config.FROZEN:
        from . import install
        install.refresh_version()
    url = server.start()
    config.INSTANCE_FILE.write_text(json.dumps({"port": int(url.rsplit(":", 1)[1]), "pid": os.getpid()}),
                                    encoding="utf-8")
    shell = Shell(url)
    server.STATE.window = shell
    shell.start_tray()
    # private_mode off + a storage folder: the page's localStorage (theme, volume, queue) survives restarts
    webview.start(gui="edgechromium", private_mode=False, storage_path=str(config.USER_DIR / "webview"),
                  debug="--debug" in sys.argv)
