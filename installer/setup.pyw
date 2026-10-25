"""MUMUsic setup: the program inside MUMUsic-Setup-<version>.exe.

Installs the app for the current user (no admin rights): files in %LOCALAPPDATA%\\Programs\\MUMUsic, Start menu
and desktop shortcuts, an entry in Settings > Apps to uninstall it. Running it again updates or repairs the app;
likes and playlists are never touched.

    MUMUsic-Setup.exe                    the setup window
    MUMUsic-Setup.exe --silent [--dir X] install without asking (the app's own updates use this, with --launch)
"""
import ctypes
import subprocess
import sys
import threading
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from mumusic import __version__, config, install

BUNDLE = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
PAYLOAD = BUNDLE / "MUMUsic.zip"
LOGO = BUNDLE / "icon.png" if getattr(sys, "frozen", False) else config.ICON_PNG
COLORS = {"bg": "#0c0d0e", "panel": "#141618", "ink": "#ebebe5", "ink2": "#a6a7a1", "ink3": "#696b67",
          "line": "#26292c", "accent": "#ffd80a"}
FONT = "Segoe UI"


class Setup:
    def __init__(self):
        self.root = tk.Tk()
        self.root.title(f"{config.APP_NAME} setup")
        self.root.configure(bg=COLORS["bg"])
        self.root.resizable(False, False)
        try:
            self.root.iconbitmap(default=str(BUNDLE / "icon.ico") if getattr(sys, "frozen", False) else str(config.ICON_ICO))
        except tk.TclError:
            pass
        existing = install.installed_dir()
        self.dir = tk.StringVar(value=str(existing or install.DEFAULT_DIR))
        self.desktop = tk.BooleanVar(value=not existing or install.shortcut_paths()["desktop"].exists())
        self.updating = existing is not None
        self.build()
        self.center(560, 380)
        self.root.update_idletasks()
        # dark title bar like the app (DWMWA_USE_IMMERSIVE_DARK_MODE)
        hwnd = ctypes.windll.user32.GetParent(self.root.winfo_id())
        ctypes.windll.dwmapi.DwmSetWindowAttribute(hwnd, 20, ctypes.byref(ctypes.c_int(1)), 4)

    def center(self, w, h):
        x = (self.root.winfo_screenwidth() - w) // 2
        y = (self.root.winfo_screenheight() - h) // 3
        self.root.geometry(f"{w}x{h}+{x}+{y}")

    def label(self, parent, text, size=10, color="ink2", bold=False, **kw):
        return tk.Label(parent, text=text, bg=COLORS["bg"], fg=COLORS[color],
                        font=(FONT, size, "bold" if bold else "normal"), **kw)

    def button(self, parent, text, command, primary=False):
        bg, fg = (COLORS["accent"], "#141414") if primary else (COLORS["panel"], COLORS["ink"])
        return tk.Button(parent, text=text, command=command, bg=bg, fg=fg, activebackground=bg, activeforeground=fg,
                         relief="flat", bd=0, padx=18, pady=7, font=(FONT, 10, "bold"), cursor="hand2")

    def build(self):
        tk.Frame(self.root, bg=COLORS["accent"], height=3).pack(fill="x")
        body = tk.Frame(self.root, bg=COLORS["bg"], padx=28, pady=22)
        body.pack(fill="both", expand=True)

        head = tk.Frame(body, bg=COLORS["bg"])
        head.pack(fill="x")
        self.logo = tk.PhotoImage(file=str(LOGO)).subsample(2)
        tk.Label(head, image=self.logo, bg=COLORS["accent"], bd=0).pack(side="left")
        titles = tk.Frame(head, bg=COLORS["bg"], padx=18)
        titles.pack(side="left", fill="y")
        self.label(titles, "MUMUSIC", 26, "ink", bold=True).pack(anchor="w")
        self.label(titles, f"Version {__version__}", 9, "ink3").pack(anchor="w")
        self.label(titles, "Anime and visual novel songs: search, playlists,\nsimilar songs and endless discovery.",
                   10, justify="left").pack(anchor="w", pady=(8, 0))

        self.options = tk.Frame(body, bg=COLORS["bg"], pady=18)
        self.options.pack(fill="x")
        self.label(self.options, "Install folder", 9, "ink3").pack(anchor="w")
        row = tk.Frame(self.options, bg=COLORS["bg"])
        row.pack(fill="x", pady=(4, 10))
        tk.Entry(row, textvariable=self.dir, bg=COLORS["panel"], fg=COLORS["ink"], relief="flat",
                 insertbackground=COLORS["ink"], font=(FONT, 10), readonlybackground=COLORS["panel"],
                 state="readonly").pack(side="left", fill="x", expand=True, ipady=6, padx=(0, 8))
        self.change = self.button(row, "Change…", self.pick_dir)
        self.change.pack(side="right")
        if self.updating:
            self.change.configure(state="disabled")
        tk.Checkbutton(self.options, text="Create a desktop shortcut", variable=self.desktop, bg=COLORS["bg"],
                       fg=COLORS["ink2"], selectcolor=COLORS["panel"], activebackground=COLORS["bg"],
                       activeforeground=COLORS["ink"], font=(FONT, 10), bd=0, highlightthickness=0).pack(anchor="w")

        style = ttk.Style()
        style.theme_use("clam")
        style.configure("M.Horizontal.TProgressbar", troughcolor=COLORS["line"], background=COLORS["accent"],
                        bordercolor=COLORS["line"], lightcolor=COLORS["accent"], darkcolor=COLORS["accent"])
        self.bar = ttk.Progressbar(body, style="M.Horizontal.TProgressbar", maximum=1.0)
        self.status = self.label(body, "", 10, "ink3", anchor="w")

        self.buttons = tk.Frame(body, bg=COLORS["bg"])
        self.buttons.pack(side="bottom", fill="x")
        self.main_button = self.button(self.buttons, "Update" if self.updating else "Install", self.start, primary=True)
        self.main_button.pack(side="right")
        self.close_button = self.button(self.buttons, "Cancel", self.root.destroy)
        self.close_button.pack(side="right", padx=(0, 8))

    def pick_dir(self):
        chosen = filedialog.askdirectory(initialdir=str(Path(self.dir.get()).parent), title="Install MUMUsic in")
        if chosen:
            path = Path(chosen)
            self.dir.set(str(path if path.name == config.APP_NAME else path / config.APP_NAME))

    def start(self):
        self.main_button.configure(state="disabled")
        self.close_button.configure(state="disabled")
        self.options.pack_forget()
        self.status.configure(text="Installing…")
        self.status.pack(fill="x", pady=(26, 6))
        self.bar.pack(fill="x")
        threading.Thread(target=self.run, daemon=True).start()

    def run(self):
        try:
            exe = install.install(PAYLOAD, Path(self.dir.get()), self.desktop.get(),
                                  lambda f: self.root.after(0, self.bar.configure, {"value": f}))
            self.root.after(0, self.done, exe)
        except Exception as e:                   # shown to the user as is
            self.root.after(0, self.failed, e)

    def done(self, exe):
        self.exe = exe
        self.status.configure(text=f"{config.APP_NAME} is installed. You'll find it in the Start menu.", fg=COLORS["ink"])
        self.main_button.configure(text=f"Start {config.APP_NAME}", command=self.launch, state="normal")
        self.close_button.configure(text="Close", state="normal")

    def failed(self, error):
        messagebox.showerror(config.APP_NAME, f"The installation failed:\n\n{error}")
        self.root.destroy()

    def launch(self):
        launch(self.exe)
        self.root.destroy()


def launch(exe):
    """The setup runs inside a job that Windows closes, with its processes, when the setup exits: the app is started
    outside of it, or by Explorer when the job does not allow that."""
    try:
        subprocess.Popen([str(exe)], cwd=str(exe.parent), close_fds=True,
                         creationflags=subprocess.CREATE_BREAKAWAY_FROM_JOB | subprocess.CREATE_NEW_PROCESS_GROUP)
    except OSError:
        subprocess.Popen(["explorer.exe", str(exe)])


def main():
    if "--silent" in sys.argv:
        target = Path(sys.argv[sys.argv.index("--dir") + 1]) if "--dir" in sys.argv else \
            install.installed_dir() or install.DEFAULT_DIR
        desktop = install.shortcut_paths()["desktop"].exists() if install.installed_dir() else True
        exe = install.install(PAYLOAD, target, desktop_shortcut=desktop and "--no-desktop" not in sys.argv)
        if "--launch" in sys.argv:
            launch(exe)
        return
    Setup().root.mainloop()


main()
