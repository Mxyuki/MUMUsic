"""Updates from GitHub: the app (release "v<version>", its one asset MUMUsic-Setup-<version>.exe) and the
encrypted library (release "library", assets manifest.json + library.mumu).

manifest.json: {"format", "version", "builtAt", "size", "sha256", "salt", "check", "songs": {"amq", "emq"}, "minApp"}
`salt` + `check` let the app verify an access key before downloading the library (crypto.key_check).
"""
import hashlib
import json
import os
import re
import subprocess
import threading
import urllib.request
from pathlib import Path

from . import __version__, config, crypto, settings, userdata
from .schema import LIBRARY_FORMAT

API = "https://api.github.com/repos/"


class UpdateError(Exception):
    pass


def version_tuple(v):
    return tuple(int(x) for x in re.findall(r"\d+", v or "")) or (0,)


def fetch(url, timeout=30):
    req = urllib.request.Request(url, headers={"User-Agent": config.USER_AGENT, "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.load(r)


def release_url(tag, asset):
    return f"https://github.com/{config.GITHUB_REPO}/releases/download/{tag}/{asset}"


def manifest():
    return fetch(release_url(config.LIBRARY_RELEASE, config.MANIFEST_ASSET))


def latest_app():
    """Newest "v<version>" release (the library release is not an app release, so "latest" can't be trusted)."""
    best = None
    for rel in fetch(f"{API}{config.GITHUB_REPO}/releases?per_page=30"):
        if rel.get("draft") or rel.get("prerelease") or not re.fullmatch(r"v\d+(\.\d+)*", rel["tag_name"]):
            continue
        if best is None or version_tuple(rel["tag_name"]) > version_tuple(best["tag_name"]):
            best = rel
    if not best:
        return None
    asset = next((a for a in best["assets"] if a["name"].startswith(config.SETUP_PREFIX)
                  and a["name"].endswith(".exe")), None)
    return {"version": best["tag_name"][1:], "notes": (best.get("body") or "").strip()[:2000],
            "page": best["html_url"], "download": asset and asset["browser_download_url"],
            "size": asset and asset["size"]}


def check():
    """What can be updated: {"app": {...} | None, "library": {...} | None}. Network errors leave a part None."""
    out = {"app": None, "library": None}
    if not config.GITHUB_REPO:
        return out
    try:
        app = latest_app()
        if app and version_tuple(app["version"]) > version_tuple(__version__) \
                and app["version"] != settings.get("skipAppVersion"):
            out["app"] = app | {"current": __version__, "canInstall": config.FROZEN and bool(app["download"])}
    except (OSError, ValueError, KeyError) as e:
        print(f"[update] app check failed: {e}", flush=True)
    if not config.DEV_LIBRARY_DB.exists():
        try:
            m = manifest()
            if m.get("version") != settings.get("libraryVersion"):
                out["library"] = m | {"current": settings.get("libraryVersion"),
                                      "needsApp": m.get("format", 1) > LIBRARY_FORMAT
                                      or version_tuple(m.get("minApp")) > version_tuple(__version__)}
        except (OSError, ValueError, KeyError) as e:
            print(f"[update] library check failed: {e}", flush=True)
    return out


# ---------------------------------------------------------------- background jobs with progress

class Job:
    """One download / install running in the background; the UI polls `state()`."""
    def __init__(self):
        self.lock = threading.Lock()
        self.status = {"kind": None, "step": "idle", "progress": 0.0, "error": None}

    def state(self):
        with self.lock:
            return dict(self.status)

    def set(self, **values):
        with self.lock:
            self.status.update(values)

    def busy(self):
        return self.state()["step"] in ("downloading", "decrypting", "installing")

    def start(self, kind, fn, *args):
        if self.busy():
            raise UpdateError("An update is already running")
        self.set(kind=kind, step="downloading", progress=0.0, error=None)

        def run():
            try:
                fn(*args)
                self.set(step="done", progress=1.0)
            except Exception as e:      # shown to the user as is
                print(f"[update] {kind} failed: {type(e).__name__}: {e}", flush=True)
                self.set(step="error", error=str(e))
        threading.Thread(target=run, daemon=True).start()


JOB = Job()


def download(url, dest, size=None, sha256=None):
    dest.parent.mkdir(parents=True, exist_ok=True)
    digest = hashlib.sha256()
    req = urllib.request.Request(url, headers={"User-Agent": config.USER_AGENT})
    with urllib.request.urlopen(req, timeout=60) as r, open(dest, "wb") as f:
        total = size or int(r.headers.get("Content-Length") or 0)
        done = 0
        while chunk := r.read(1 << 20):
            f.write(chunk)
            digest.update(chunk)
            done += len(chunk)
            if total:
                JOB.set(progress=min(done / total, 1.0))
    if sha256 and digest.hexdigest() != sha256:
        dest.unlink(missing_ok=True)
        raise UpdateError("The download is damaged (checksum mismatch), try again")


def install_library(passphrase, on_done, source_file=None, info=None):
    """Download (or take `source_file`), decrypt into the user folder, then call on_done() to reload.
    `info` is the manifest when it comes from GitHub."""
    encrypted = Path(source_file) if source_file else config.DOWNLOAD_DIR / config.LIBRARY_ASSET
    if not source_file:
        download(release_url(config.LIBRARY_RELEASE, config.LIBRARY_ASSET), encrypted, info.get("size"),
                 info.get("sha256"))
    userdata.backup("before-library-update")
    JOB.set(step="decrypting", progress=0.0)
    new = config.LIBRARY_DB.with_suffix(".new")
    crypto.decrypt_file(encrypted, new, passphrase, progress=lambda p: JOB.set(progress=p))
    os.replace(new, config.LIBRARY_DB)
    if not source_file:
        encrypted.unlink(missing_ok=True)
    settings.update(key=crypto.protect(passphrase), libraryVersion=(info or {}).get("version") or "local")
    JOB.set(step="installing")
    on_done()


def install_app(info, quit_app):
    """Packaged app only: download the new setup and run it silently. It waits for this app to quit, replaces the
    program files and starts the new version. User data lives in the user folder, so nothing else is touched."""
    userdata.backup("before-app-update")
    setup = config.DOWNLOAD_DIR / f"{config.SETUP_PREFIX}{info['version']}.exe"
    download(info["download"], setup, info.get("size"))
    JOB.set(step="installing")
    # outside this process's job, so it keeps running once the app has quit
    subprocess.Popen([str(setup), "--silent", "--launch"], cwd=str(config.DOWNLOAD_DIR), close_fds=True,
                     creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.CREATE_BREAKAWAY_FROM_JOB)
    quit_app()
