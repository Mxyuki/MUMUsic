"""Windows integration of the installed app: Start menu and desktop shortcuts, the entry in Settings > Apps, and
uninstalling. Used by the setup program (installer/setup.pyw) and by `MUMUsic.exe --uninstall`.

Installed per user (no admin rights): the app in %LOCALAPPDATA%\\Programs\\MUMUsic, the uninstall entry in HKCU.
The user's data (%APPDATA%\\MUMUsic) is only removed when the user asks for it while uninstalling.
"""
import ctypes
import json
import os
import shutil
import subprocess
import urllib.request
import winreg
from pathlib import Path

from . import __version__, config

DEFAULT_DIR = Path(os.environ.get("LOCALAPPDATA") or Path.home()) / "Programs" / config.APP_NAME
EXE_NAME = f"{config.APP_NAME}.exe"
UNINSTALL_KEY = rf"Software\Microsoft\Windows\CurrentVersion\Uninstall\{config.APP_NAME}"
CSIDL_DESKTOP, CSIDL_PROGRAMS = 0x10, 0x02
MB_YESNO, MB_ICONQUESTION, MB_ICONINFO, IDYES = 0x4, 0x20, 0x40, 6


def shell_folder(csidl):
    """The real Desktop / Start menu folder (they can be redirected, e.g. to OneDrive)."""
    buf = ctypes.create_unicode_buffer(260)
    ctypes.windll.shell32.SHGetFolderPathW(None, csidl, None, 0, buf)
    return Path(buf.value)


def shortcut_paths():
    name = f"{config.APP_NAME}.lnk"
    return {"start": shell_folder(CSIDL_PROGRAMS) / name, "desktop": shell_folder(CSIDL_DESKTOP) / name}


def create_shortcut(link, target):
    """A .lnk through the Windows Script Host (comes with Windows, no extra package)."""
    q = lambda p: str(p).replace("'", "''")    # noqa: E731  (PowerShell single-quoted string)
    script = (f"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{q(link)}'); "
              f"$s.TargetPath = '{q(target)}'; $s.WorkingDirectory = '{q(target.parent)}'; "
              f"$s.IconLocation = '{q(target)},0'; $s.Description = '{config.APP_NAME}'; $s.Save()")
    subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", script], check=True,
                   creationflags=subprocess.CREATE_NO_WINDOW)


def installed_dir():
    """Where the app is installed, or None."""
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY) as key:
            return Path(winreg.QueryValueEx(key, "InstallLocation")[0])
    except OSError:
        return None


def folder_size_kb(path):
    return sum(f.stat().st_size for f in path.rglob("*") if f.is_file()) // 1024


def register(install_dir, version):
    exe = install_dir / EXE_NAME
    values = {
        "DisplayName": config.APP_NAME, "DisplayVersion": version, "Publisher": config.APP_NAME,
        "DisplayIcon": f"{exe},0", "InstallLocation": str(install_dir),
        "UninstallString": f'"{exe}" --uninstall', "NoModify": 1, "NoRepair": 1,
        "EstimatedSize": folder_size_kb(install_dir),
    }
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY) as key:
        for name, value in values.items():
            winreg.SetValueEx(key, name, 0, winreg.REG_DWORD if isinstance(value, int) else winreg.REG_SZ, value)


def refresh_version():
    """After a self-update the files are new but the Apps entry still shows the old version."""
    if config.FROZEN and installed_dir() == config.INSTALL_DIR:
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY, 0, winreg.KEY_SET_VALUE) as key:
                winreg.SetValueEx(key, "DisplayVersion", 0, winreg.REG_SZ, __version__)
        except OSError:
            pass


def close_running_app(timeout=10):
    """Ask a running MUMUsic to quit (its local server), then make sure it is gone."""
    try:
        port = json.loads(config.INSTANCE_FILE.read_text(encoding="utf-8"))["port"]
        req = urllib.request.Request(f"http://127.0.0.1:{port}/api/window/quit", data=b"{}", method="POST")
        urllib.request.urlopen(req, timeout=3).close()
    except (OSError, ValueError, KeyError):
        pass
    others = ["/FI", f"IMAGENAME eq {EXE_NAME}", "/FI", f"PID ne {os.getpid()}"]    # not the uninstaller itself
    for _ in range(timeout * 2):
        running = subprocess.run(["tasklist", *others, "/NH"], capture_output=True, text=True,
                                 creationflags=subprocess.CREATE_NO_WINDOW).stdout
        if EXE_NAME.lower() not in running.lower():
            return
        ctypes.windll.kernel32.Sleep(500)
    subprocess.run(["taskkill", *others, "/F"], capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)


def ask(text, icon=MB_ICONQUESTION):
    return ctypes.windll.user32.MessageBoxW(None, text, config.APP_NAME, MB_YESNO | icon) == IDYES


def uninstall():
    """`MUMUsic.exe --uninstall` (Settings > Apps). The folder can't be deleted by the exe inside it, so a small
    script removes it once this process has exited."""
    if not ask(f"Uninstall {config.APP_NAME}?"):
        return
    keep_data = not ask("Also delete your likes, playlists and history?\n\n"
                        "Choose No to keep them for a later install.")
    close_running_app()
    for link in shortcut_paths().values():
        link.unlink(missing_ok=True)
    try:
        winreg.DeleteKey(winreg.HKEY_CURRENT_USER, UNINSTALL_KEY)
    except OSError:
        pass
    targets = [config.INSTALL_DIR] + ([] if keep_data else [config.USER_DIR])
    temp = Path(os.environ.get("TEMP") or Path.home())
    script = temp / f"mumusic-uninstall-{os.getpid()}.cmd"
    # retried: a file can stay locked a moment after the exit (antivirus scan)
    removals = "".join(f'rmdir /s /q "{t}" 2>nul\nif exist "{t}" set left=1\n' for t in targets)
    script.write_text("@echo off\n"
                      f':wait\ntasklist /FI "PID eq {os.getpid()}" 2>nul | find "{os.getpid()}" >nul '
                      "&& (ping -n 2 127.0.0.1 >nul & goto wait)\n"
                      "set tries=0\n:remove\nset left=\n" + removals +
                      "set /a tries+=1\nif defined left if %tries% lss 15 (ping -n 2 127.0.0.1 >nul & goto remove)\n"
                      'del "%~f0"\n', encoding="utf-8")
    # started from TEMP: a process whose current folder is the install folder would keep it from being deleted
    subprocess.Popen(["cmd", "/c", str(script)], cwd=str(temp), close_fds=True,
                     creationflags=subprocess.CREATE_NO_WINDOW | subprocess.CREATE_NEW_PROCESS_GROUP
                     | subprocess.CREATE_BREAKAWAY_FROM_JOB)
    ctypes.windll.user32.MessageBoxW(None, f"{config.APP_NAME} has been uninstalled.", config.APP_NAME, MB_ICONINFO)


def install(payload_zip, install_dir, desktop_shortcut, progress=lambda f: None):
    """Setup: replace the app folder with the payload, then shortcuts and the Apps entry. Returns the exe."""
    import zipfile

    from . import userdata
    close_running_app()
    userdata.backup("before-setup")
    staging = install_dir.with_name(install_dir.name + ".new")
    shutil.rmtree(staging, ignore_errors=True)
    with zipfile.ZipFile(payload_zip) as z:
        names = z.namelist()
        for n, name in enumerate(names, 1):
            # the zip holds one top folder (MUMUsic/...): its content goes straight into the install folder
            rel = name.split("/", 1)[1] if "/" in name else ""
            if rel:
                target = staging / rel
                if name.endswith("/"):
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    with z.open(name) as src, open(target, "wb") as dst:
                        shutil.copyfileobj(src, dst)
            if n % 50 == 0 or n == len(names):
                progress(0.9 * n / len(names))
    if install_dir.exists():
        shutil.rmtree(install_dir)
    staging.rename(install_dir)
    exe = install_dir / EXE_NAME
    links = shortcut_paths()
    create_shortcut(links["start"], exe)
    if desktop_shortcut:
        create_shortcut(links["desktop"], exe)
    else:
        links["desktop"].unlink(missing_ok=True)
    register(install_dir, __version__)
    progress(1.0)
    return exe

