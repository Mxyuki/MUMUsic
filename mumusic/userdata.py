"""The user's own data in user.db: likes, playlists and play history, plus JSON export / import.
Song ids are the library ids ("a123", "e456"). One connection per thread (the HTTP server is threaded)."""
import sqlite3
import threading
from datetime import datetime, timezone

from . import config

SCHEMA = """
CREATE TABLE IF NOT EXISTS likes (
    songId  TEXT PRIMARY KEY,
    likedAt TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS playlists (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    name      TEXT NOT NULL,
    createdAt TEXT,
    updatedAt TEXT
);
CREATE TABLE IF NOT EXISTS playlistSongs (
    playlistId INTEGER NOT NULL,
    songId     TEXT    NOT NULL,
    position   REAL    NOT NULL,
    addedAt    TEXT,
    PRIMARY KEY (playlistId, songId)
);
CREATE TABLE IF NOT EXISTS plays (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    songId   TEXT NOT NULL,
    playedAt TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_plays_song ON plays(songId);
"""

_local = threading.local()


def now():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


KEEP_BACKUPS = 10


def backup(reason=None):
    """Copy of user.db: user.backup.db at every start, and backups/user-<time>-<reason>.db before an update
    (the last 10 are kept). Made with SQLite's backup API, so changes still in the -wal file are included."""
    if not config.USER_DB.exists():
        return None
    if reason:
        folder = config.USER_DIR / "backups"
        folder.mkdir(exist_ok=True)
        dest = folder / f"user-{datetime.now().strftime('%Y%m%d-%H%M%S')}-{reason}.db"
    else:
        dest = config.USER_DB.with_name("user.backup.db")
    src, out = sqlite3.connect(config.USER_DB, timeout=30), sqlite3.connect(dest)
    try:
        src.backup(out)
    finally:
        out.close()
        src.close()
    if reason:
        for old in sorted(dest.parent.glob("user-*.db"))[:-KEEP_BACKUPS]:
            old.unlink()
    return dest


def db():
    if not hasattr(_local, "db"):
        config.USER_DIR.mkdir(parents=True, exist_ok=True)
        c = sqlite3.connect(config.USER_DB, timeout=30)
        c.row_factory = sqlite3.Row
        c.execute("PRAGMA journal_mode=WAL")
        c.executescript(SCHEMA)
        _local.db = c
    return _local.db


# ---------------------------------------------------------------- likes, plays

def liked_ids():
    return [r[0] for r in db().execute("SELECT songId FROM likes ORDER BY likedAt DESC")]


def is_liked(sid):
    return db().execute("SELECT 1 FROM likes WHERE songId = ?", (sid,)).fetchone() is not None


def like(sid, liked):
    c = db()
    with c:
        if liked:
            c.execute("INSERT OR IGNORE INTO likes VALUES (?,?)", (sid, now()))
        else:
            c.execute("DELETE FROM likes WHERE songId = ?", (sid,))


def add_play(sid):
    c = db()
    with c:
        c.execute("INSERT INTO plays (songId, playedAt) VALUES (?,?)", (sid, now()))


def recent_ids(limit):
    return [r[0] for r in db().execute(
        "SELECT songId FROM plays GROUP BY songId ORDER BY MAX(id) DESC LIMIT ?", (limit,))]


# ---------------------------------------------------------------- playlists

def playlists():
    return [dict(r) for r in db().execute("""
        SELECT p.id, p.name, p.createdAt, p.updatedAt, COUNT(ps.songId) AS count
        FROM playlists p LEFT JOIN playlistSongs ps ON ps.playlistId = p.id
        GROUP BY p.id ORDER BY p.updatedAt DESC""")]


def playlist(pid):
    row = db().execute("SELECT * FROM playlists WHERE id = ?", (pid,)).fetchone()
    return dict(row) if row else None


def playlist_song_ids(pid):
    return [r[0] for r in db().execute(
        "SELECT songId FROM playlistSongs WHERE playlistId = ? ORDER BY position", (pid,))]


def find_playlist(name):
    row = db().execute("SELECT id FROM playlists WHERE name = ?", (name,)).fetchone()
    return row[0] if row else None


def create_playlist(name, ids=(), created_at=None):
    c = db()
    with c:
        pid = c.execute("INSERT INTO playlists (name, createdAt, updatedAt) VALUES (?,?,?)",
                        (name, created_at or now(), now())).lastrowid
        _add(c, pid, ids)
    return pid


def add_to_playlist(pid, ids):
    c = db()
    with c:
        return _add(c, pid, ids)


def fill_playlist(name, ids):
    """Creates the playlist, or adds to the one of that name what it misses. (id, created, songs added)."""
    pid = find_playlist(name)
    if pid is None:
        return create_playlist(name, ids), True, len(ids)
    return pid, False, add_to_playlist(pid, ids)


def _add(c, pid, ids):
    pos = c.execute("SELECT COALESCE(MAX(position), 0) FROM playlistSongs WHERE playlistId = ?", (pid,)).fetchone()[0]
    added = 0
    for sid in ids:
        pos += 1
        added += c.execute("INSERT OR IGNORE INTO playlistSongs VALUES (?,?,?,?)", (pid, sid, pos, now())).rowcount
    _touch(c, pid)
    return added


def _touch(c, pid):
    c.execute("UPDATE playlists SET updatedAt = ? WHERE id = ?", (now(), pid))


def rename_playlist(pid, name):
    c = db()
    with c:
        c.execute("UPDATE playlists SET name = ?, updatedAt = ? WHERE id = ?", (name, now(), pid))


def delete_playlist(pid):
    c = db()
    with c:
        c.execute("DELETE FROM playlistSongs WHERE playlistId = ?", (pid,))
        c.execute("DELETE FROM playlists WHERE id = ?", (pid,))


def remove_from_playlist(pid, sid):
    c = db()
    with c:
        c.execute("DELETE FROM playlistSongs WHERE playlistId = ? AND songId = ?", (pid, sid))
        _touch(c, pid)


def reorder_playlist(pid, ids):
    c = db()
    with c:
        c.executemany("UPDATE playlistSongs SET position = ? WHERE playlistId = ? AND songId = ?",
                      [(pos, pid, sid) for pos, sid in enumerate(ids)])
        _touch(c, pid)


# ---------------------------------------------------------------- export / import

def export_data(lib):
    def ref(sid):
        s = lib.songs.get(sid, {})
        return {"id": sid, "file": s.get("file"), "name": s.get("name"), "artist": s.get("artist"),
                "work": s.get("work")}
    c = db()
    return {
        "app": config.APP_NAME, "version": 2, "exportedAt": now(),
        "likes": [ref(r[0]) | {"likedAt": r[1]} for r in c.execute("SELECT songId, likedAt FROM likes ORDER BY likedAt")],
        "playlists": [{"name": p["name"], "createdAt": p["createdAt"], "songs": [ref(i) for i in playlist_song_ids(p["id"])]}
                      for p in playlists()],
    }


def import_data(lib, data):
    """Merges an export file. Also reads the exports of the older AMQ / EMQ players, whose ids were plain numbers:
    songs are then found by file name, else by title + artist."""
    if not isinstance(data, dict) or ("likes" not in data and "playlists" not in data):
        raise ValueError("Not a MUMUsic export file")
    by_title = {}
    for s in lib.songs.values():
        by_title.setdefault(s["titleKey"], s["id"])

    def resolve(ref):
        if not isinstance(ref, dict):
            ref = {"id": ref}
        sid = ref.get("id")
        if isinstance(sid, str) and sid in lib.songs:
            return sid
        if ref.get("file") in lib.file_to_id:
            return lib.file_to_id[ref["file"]]
        return by_title.get(f"{(ref.get('name') or '').lower()}|{(ref.get('artist') or '').lower()}")

    stats = {"likes": 0, "playlists": 0, "songs": 0, "missing": 0}
    c = db()
    with c:
        for ref in data.get("likes", []):
            sid = resolve(ref)
            if sid is None:
                stats["missing"] += 1
                continue
            liked_at = ref.get("likedAt") if isinstance(ref, dict) else None
            stats["likes"] += c.execute("INSERT OR IGNORE INTO likes VALUES (?,?)", (sid, liked_at or now())).rowcount
    for p in data.get("playlists", []):
        ids = []
        for ref in p.get("songs", []):
            sid = resolve(ref)
            if sid is None:
                stats["missing"] += 1
            else:
                ids.append(sid)
        name = (p.get("name") or "Imported playlist").strip()
        pid = find_playlist(name)
        if pid is None:
            create_playlist(name, ids, p.get("createdAt"))
            stats["playlists"] += 1
            stats["songs"] += len(set(ids))
        else:
            stats["songs"] += add_to_playlist(pid, ids)
    return stats
