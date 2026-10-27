"""Local HTTP server behind the window: the web UI, the JSON API and the EMQ media proxy.

Listens on 127.0.0.1 on a free port. The library loads in the background: until it is ready, /api/app says what
is going on (loading, or setup needed: first start without a library) and the other API calls answer 503.
"""
import json
import re
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, unquote, urlparse

import requests

from . import __version__, anilist, config, crypto, settings, updater, userdata, vndb
from . import discord
from .discord import PRESENCE
from .emq_media import EmqMedia
from .library import Library

SONG = r"([ae]\d+)"
PERSON = r"([age]\d+)"
STATIC_TYPES = {".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8",
                ".js": "text/javascript; charset=utf-8", ".svg": "image/svg+xml", ".png": "image/png",
                ".ico": "image/x-icon", ".woff2": "font/woff2"}
MEDIA_HEADERS = ("Content-Type", "Content-Length", "Content-Range", "Content-Encoding", "Accept-Ranges",
                 "Last-Modified", "ETag")


class ApiError(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def need(cond, status=404, message="Not found"):
    if not cond:
        raise ApiError(status, message)


# ---------------------------------------------------------------- app state

class State:
    def __init__(self):
        self.lib = None
        self.status = "loading"          # loading | setup | ready | error
        self.error = None
        self.updates = None              # result of updater.check(), once per run
        self.window = None               # set by window.py: show / quit hooks
        self.lock = threading.Lock()

    def library_path(self):
        if config.DEV_LIBRARY_DB.exists():
            return config.DEV_LIBRARY_DB
        return config.LIBRARY_DB if config.LIBRARY_DB.exists() else None

    def load(self, wait=False):
        """(Re)load the library, in the background unless `wait`. The old one stays in use until the new one is
        ready."""
        def run():
            path = self.library_path()
            if path is None:
                self.status = "setup"
                return
            try:
                lib = Library(path, settings.get("sources"), settings.get("skipKinds"))
            except Exception as e:
                print(f"[library] could not load {path}: {type(e).__name__}: {e}", flush=True)
                self.status, self.error = ("setup", None) if path == config.LIBRARY_DB else ("error", str(e))
                return
            self.lib, self.status, self.error = lib, "ready", None
            print(f"[library] {len(lib.songs)} songs, {len(lib.emb_ids)} analysed", flush=True)
        if wait:
            run()
        else:
            threading.Thread(target=run, daemon=True).start()

    def reload(self):
        """For update jobs: their "done" means the new library is in use."""
        self.load(wait=True)


STATE = State()
MEDIA = EmqMedia()


def lib():
    need(STATE.lib is not None, 503, "The library is still loading")
    return STATE.lib


# ---------------------------------------------------------------- routes

ROUTES = []


def route(method, pattern):
    def deco(fn):
        ROUTES.append((method, re.compile("^" + pattern + "$"), fn))
        return fn
    return deco


@route("GET", r"/api/app")
def api_app(req):
    l = STATE.lib
    return {
        "name": config.APP_NAME, "version": __version__, "status": STATE.status, "error": STATE.error,
        "sources": sorted(l.sources) if l else settings.get("sources"),
        "skipKinds": sorted(l.skip_kinds) if l else settings.get("skipKinds"),
        "kindCounts": l and l.kind_counts(),
        "library": l and {"version": l.meta.get("version"), "builtAt": l.meta.get("builtAt"),
                          "dev": l.path == config.DEV_LIBRARY_DB},
        "online": bool(config.GITHUB_REPO), "keySaved": bool(settings.get("key")),
        "discord": discord_state(),
        "job": updater.JOB.state(),
    }


@route("POST", r"/api/setup")
def api_setup(req):
    """First start: check the access key, then download + decrypt the library (or decrypt a .mumu file picked
    by the user). Progress: GET /api/job."""
    body = req.json()
    key, file = (body.get("key") or "").strip(), body.get("file")
    need(key, 400, "Enter the access key")
    if file:
        updater.JOB.start("library", updater.install_library, key, STATE.reload, file)
        return {"ok": True}
    need(config.GITHUB_REPO, 400, "This build has no online library: pick a library file instead")
    try:
        info = updater.manifest()
    except (OSError, ValueError) as e:
        raise ApiError(502, f"Could not reach GitHub: {e}")
    need(crypto.is_right_key(key, info["salt"], info["check"]), 400, "Wrong access key")
    updater.JOB.start("library", updater.install_library, key, STATE.reload, None, info)
    return {"ok": True, "version": info.get("version")}


@route("GET", r"/api/job")
def api_job(req):
    return updater.JOB.state() | {"status": STATE.status}


@route("GET", r"/api/updates")
def api_updates(req):
    if STATE.updates is None or req.query.get("refresh"):
        STATE.updates = updater.check()
    return STATE.updates


@route("POST", r"/api/updates/library")
def api_update_library(req):
    info = (STATE.updates or {}).get("library")
    need(info, 400, "No library update available")
    need(not info.get("needsApp"), 400, "This library needs a newer version of the app")
    key = crypto.unprotect(settings.get("key") or "")
    need(key, 400, "No access key saved")
    if not crypto.is_right_key(key, info["salt"], info["check"]):
        settings.update(key=None)
        raise ApiError(400, "The access key changed: enter the new one")

    def done():
        STATE.updates["library"] = None
        STATE.reload()
    updater.JOB.start("library", updater.install_library, key, done, None, info)
    return {"ok": True}


@route("POST", r"/api/updates/key")
def api_update_key(req):
    """A new access key for the library update (when the old one stopped working)."""
    info = (STATE.updates or {}).get("library")
    key = (req.json().get("key") or "").strip()
    need(info and key, 400, "Enter the access key")
    need(crypto.is_right_key(key, info["salt"], info["check"]), 400, "Wrong access key")
    settings.update(key=crypto.protect(key))
    return api_update_library(req)


@route("POST", r"/api/updates/app")
def api_update_app(req):
    info = (STATE.updates or {}).get("app")
    need(info and info.get("canInstall"), 400, "No app update to install")
    updater.JOB.start("app", updater.install_app, info, lambda: STATE.window and STATE.window.quit())
    return {"ok": True}


@route("POST", r"/api/updates/skip")
def api_update_skip(req):
    settings.update(skipAppVersion=req.json().get("version"))
    if STATE.updates:
        STATE.updates["app"] = None
    return {"ok": True}


@route("PUT", r"/api/sources")
def api_sources(req):
    sources = [s for s in req.json().get("sources", []) if s in config.SOURCES]
    need(sources, 400, "Pick at least one library")
    settings.update(sources=sources)
    if STATE.lib:
        STATE.lib.set_sources(sources, STATE.lib.skip_kinds)
    return {"ok": True}


@route("PUT", r"/api/song-kinds")
def api_song_kinds(req):
    known = {f"{src}:{kind}" for src, kinds in config.SONG_KINDS.items() for kind in kinds}
    skip = sorted({k for k in req.json().get("skip", []) if k in known})
    for src, kinds in config.SONG_KINDS.items():
        need(len([k for k in kinds if f"{src}:{k}" not in skip]), 400, "Keep at least one kind of song on")
    settings.update(skipKinds=skip)
    if STATE.lib:
        STATE.lib.set_sources(STATE.lib.sources, skip)
    return {"skip": skip}


@route("POST", r"/api/window/show")
def api_window_show(req):
    """A second start of the app asks the running one to show its window."""
    if STATE.window:
        STATE.window.show()
    return {"ok": True}


@route("POST", r"/api/window/quit")
def api_window_quit(req):
    """The setup closes a running app before replacing its files."""
    if STATE.window:
        threading.Timer(0.2, STATE.window.quit).start()
    return {"ok": True}


@route("POST", r"/api/presence")
def api_presence(req):
    """The player reports what plays (or null) on every play / pause / seek / song change."""
    song = req.json().get("song")
    PRESENCE.update(song if song and settings.get("discord") else None)
    return {"ok": True}


def discord_state():
    return {"available": PRESENCE.available, "on": settings.get("discord"), "appId": discord.client_id(),
            "custom": bool(settings.get("discordAppId")), "status": PRESENCE.status}


@route("PUT", r"/api/discord")
def api_discord(req):
    """{"on": bool} switches the status on / off; {"appId": "..."} sets the application id after testing it
    against the running Discord ("" goes back to the one built into the app)."""
    body = req.json()
    if "appId" in body:
        app_id = (body.get("appId") or "").strip()
        if app_id:
            try:
                discord.test(app_id)
            except discord.DiscordError as e:
                raise ApiError(400, str(e))
        settings.update(discordAppId=app_id or None, discord=True)
        PRESENCE.reset()
    if "on" in body:
        settings.update(discord=bool(body["on"]))
        if not body["on"]:
            PRESENCE.update(None)
    return discord_state()


@route("GET", r"/api/home")
def api_home(req):
    l = lib()
    liked = userdata.liked_ids()
    return {
        "stats": stats(l, liked),
        "recent": l.views(userdata.recent_ids(16)),
        "recommended": l.recommend(liked, 12) if liked else [],
        "discover": l.random_picks(),
        "tags": l.tag_counts(),
    }


def stats(l, liked):
    active = [s for s in l.songs.values() if s["src"] in l.sources]
    return {"songs": len(active), "analysed": len(l.active_ids), "liked": len(liked),
            "playlists": len(userdata.playlists()),
            "bySource": {src: sum(1 for s in active if s["src"] == src) for src in sorted(l.sources)}}


@route("GET", r"/api/search")
def api_search(req):
    return lib().search(req.query.get("q", [""])[0])


@route("GET", r"/api/song/" + SONG)
def api_song(req, sid):
    d = lib().song_detail(sid)
    need(d)
    return d | {"liked": userdata.is_liked(sid)}


@route("POST", r"/api/songs")
def api_songs(req):
    return {"songs": lib().views([str(i) for i in req.json().get("ids", [])][:2000])}


@route("GET", r"/api/song/" + SONG + r"/similar")
def api_similar(req, sid):
    need(sid in lib().songs)
    return {"songs": lib().similar(sid, int(req.query.get("n", ["30"])[0]))}


@route("GET", r"/api/resolve")
def api_resolve(req):
    sid = lib().resolve(req.query.get("q", [""])[0])
    need(sid)
    return {"id": sid}


@route("GET", r"/api/person/" + PERSON)
def api_person(req, pid):
    page = lib().person_page(pid)
    need(page)
    return page


@route("GET", r"/api/work/" + SONG + r"/site")
def api_work_site(req, wid):
    url = lib().work_site(wid)
    need(url)
    return {"url": url}


@route("GET", r"/api/work/" + SONG)
def api_work(req, wid):
    page = lib().work_page(wid)
    need(page)
    return page


@route("GET", r"/api/tag/(.+)")
def api_tag(req, tag):
    return lib().tag_page(unquote(tag))


@route("GET", r"/api/tags")
def api_tags(req):
    return {"tags": lib().tag_counts()}


@route("GET", r"/api/likes")
def api_likes(req):
    ids = userdata.liked_ids()
    return {"ids": ids, "songs": lib().views(ids)}


@route("POST", r"/api/likes/" + SONG)
def api_like(req, sid):
    need(sid in lib().songs)
    userdata.like(sid, True)
    return {"ok": True}


@route("DELETE", r"/api/likes/" + SONG)
def api_unlike(req, sid):
    userdata.like(sid, False)
    return {"ok": True}


@route("GET", r"/api/recommended")
def api_recommended(req):
    seed = req.query.get("seed", [None])[0]
    return {"songs": lib().recommend(userdata.liked_ids(), int(req.query.get("n", ["60"])[0]),
                                     int(seed) if seed else None)}


@route("POST", r"/api/radio/next")
def api_radio(req):
    body = req.json()
    seed = str(body["seed"])
    current = str(body.get("current") or seed)
    song = lib().radio_next(seed, current, [str(i) for i in body.get("history", [])][-600:],
                            body.get("mode", "close"), [str(i) for i in body.get("skipped", [])],
                            [str(i) for i in body.get("liked", [])])
    need(song, 404, "No more similar songs found")
    return song


@route("GET", r"/api/playlists")
def api_playlists(req):
    return {"playlists": userdata.playlists()}


@route("POST", r"/api/playlists")
def api_playlist_create(req):
    body = req.json()
    name = (body.get("name") or "").strip() or "New playlist"
    ids = [str(i) for i in body.get("ids") or [] if str(i) in lib().songs]
    return {"id": userdata.create_playlist(name, ids)}


@route("POST", r"/api/anilist")
def api_anilist(req):
    body = req.json()
    try:
        return anilist.import_list(lib(), body.get("user") or "", body.get("statuses") or [])
    except anilist.AniListError as e:
        raise ApiError(e.status, str(e))


@route("POST", r"/api/vndb")
def api_vndb(req):
    body = req.json()
    try:
        return vndb.import_list(lib(), body.get("user") or "", body.get("labels") or [], bool(body.get("bgm", True)))
    except vndb.VndbError as e:
        raise ApiError(e.status, str(e))


@route("GET", r"/api/playlists/(\d+)")
def api_playlist(req, pid):
    p = userdata.playlist(int(pid))
    need(p)
    return p | {"songs": lib().views(userdata.playlist_song_ids(int(pid)))}


@route("PATCH", r"/api/playlists/(\d+)")
def api_playlist_rename(req, pid):
    name = (req.json().get("name") or "").strip()
    need(name, 400, "Name required")
    userdata.rename_playlist(int(pid), name)
    return {"ok": True}


@route("DELETE", r"/api/playlists/(\d+)")
def api_playlist_delete(req, pid):
    userdata.delete_playlist(int(pid))
    return {"ok": True}


@route("POST", r"/api/playlists/(\d+)/songs")
def api_playlist_add(req, pid):
    need(userdata.playlist(int(pid)))
    ids = [str(i) for i in req.json().get("ids", []) if str(i) in lib().songs]
    return {"added": userdata.add_to_playlist(int(pid), ids)}


@route("DELETE", r"/api/playlists/(\d+)/songs/" + SONG)
def api_playlist_remove(req, pid, sid):
    userdata.remove_from_playlist(int(pid), sid)
    return {"ok": True}


@route("PUT", r"/api/playlists/(\d+)/order")
def api_playlist_order(req, pid):
    userdata.reorder_playlist(int(pid), [str(i) for i in req.json().get("ids", [])])
    return {"ok": True}


@route("POST", r"/api/played")
def api_played(req):
    userdata.add_play(str(req.json()["id"]))
    return {"ok": True}


@route("GET", r"/api/recent")
def api_recent(req):
    return {"songs": lib().views(userdata.recent_ids(100))}


@route("GET", r"/api/export")
def api_export(req):
    return userdata.export_data(lib())


@route("POST", r"/api/import")
def api_import(req):
    try:
        return userdata.import_data(lib(), req.json())
    except (ValueError, KeyError, TypeError, AttributeError) as e:
        raise ApiError(400, f"Could not import: {e}")


# ---------------------------------------------------------------- http

class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass

    def json(self):
        length = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(length) or b"{}")

    def send(self, status, body, ctype="application/json; charset=utf-8"):
        data = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def media(self, path):
        """/media/<file> -> the EMQ file, Range requests passed through so seeking works."""
        path = unquote(path)
        l = STATE.lib
        if not l or l.songs.get(l.file_to_id.get(path), {}).get("src") != "emq":     # only the library's own files
            return self.send(404, {"error": "Unknown file"})
        try:
            r = MEDIA.get(path, {"Range": self.headers["Range"]} if self.headers.get("Range") else {})
        except requests.RequestException as e:
            return self.send(502, {"error": f"EMQ: {e}"})
        with r:
            if r.status_code >= 400:
                return self.send(r.status_code, {"error": f"EMQ answered {r.status_code}"})
            self.send_response(r.status_code)
            for k in MEDIA_HEADERS:
                if k in r.headers:
                    self.send_header(k, r.headers[k])
            if "Accept-Ranges" not in r.headers:
                self.send_header("Accept-Ranges", "bytes")
            self.send_header("Cache-Control", "private, max-age=86400")
            if "Content-Length" not in r.headers:
                self.send_header("Connection", "close")
                self.close_connection = True
            self.end_headers()
            try:
                for chunk in r.raw.stream(65536, decode_content=False):
                    self.wfile.write(chunk)
            except (OSError, requests.RequestException):     # the player stopped reading (seek, next song)
                self.close_connection = True

    def dispatch(self, method):
        url = urlparse(self.path)
        self.query = parse_qs(url.query)
        if url.path.startswith("/media/") and method == "GET":
            return self.media(url.path[len("/media/"):])
        if url.path.startswith("/api/"):
            for m, rx, fn in ROUTES:
                match = rx.match(url.path)
                if m == method and match:
                    try:
                        self.send(200, fn(self, *match.groups()))
                    except ApiError as e:
                        self.send(e.status, {"error": str(e)})
                    except updater.UpdateError as e:
                        self.send(409, {"error": str(e)})
                    except Exception as e:
                        print(f"[error] {method} {url.path}: {type(e).__name__}: {e}", flush=True)
                        self.send(500, {"error": f"{type(e).__name__}: {e}"})
                    return
            return self.send(404, {"error": "Unknown endpoint"})
        if method != "GET":
            return self.send(405, {"error": "Method not allowed"})
        path = (config.WEB_DIR / url.path.lstrip("/")).resolve()
        if url.path == "/" or not path.is_file() or config.WEB_DIR not in path.parents:
            path = config.WEB_DIR / "index.html"
        self.send(200, path.read_bytes(), STATIC_TYPES.get(path.suffix, "application/octet-stream"))

    def do_GET(self):
        self.dispatch("GET")

    def do_POST(self):
        self.dispatch("POST")

    def do_PUT(self):
        self.dispatch("PUT")

    def do_PATCH(self):
        self.dispatch("PATCH")

    def do_DELETE(self):
        self.dispatch("DELETE")


class Server(ThreadingHTTPServer):
    # Windows lets a second socket bind a port already in use when SO_REUSEADDR is set
    allow_reuse_address = False
    daemon_threads = True


def start():
    """Starts the server in a background thread and the library loading. Returns the base URL.
    The port is kept between runs: the window's storage (theme, volume, queue) belongs to the page's origin."""
    userdata.backup()
    userdata.db()
    try:
        server = Server(("127.0.0.1", settings.get("port") or 0), Handler)
    except OSError:
        server = Server(("127.0.0.1", 0), Handler)
    port = server.server_address[1]
    if port != settings.get("port"):
        settings.update(port=port)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    STATE.load()
    return f"http://127.0.0.1:{port}"
