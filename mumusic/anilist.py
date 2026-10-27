"""Playlist from an AniList anime list: every song of every anime on it (AMQ songs only)."""
import json
import re
import urllib.error
import urllib.request

from . import config, userdata

API = "https://graphql.anilist.co"
# Watch statuses an AniList entry can have (custom lists only regroup entries, every entry has one of these).
STATUSES = ("CURRENT", "COMPLETED", "REPEATING", "PAUSED", "PLANNING", "DROPPED")
QUERY = """query ($user: String) { MediaListCollection(userName: $user, type: ANIME) {
               lists { entries { status media { id } } } } }"""


class AniListError(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def entries(user):
    """[(AniList media id, watch status)] of a public list."""
    req = urllib.request.Request(API, data=json.dumps({"query": QUERY, "variables": {"user": user}}).encode(),
                                 headers={"Content-Type": "application/json", "Accept": "application/json",
                                          "User-Agent": config.USER_AGENT})   # AniList blocks Python's default one
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            data = json.load(r)
    except urllib.error.HTTPError as e:
        try:
            msg = json.loads(e.read())["errors"][0]["message"]
        except (ValueError, KeyError, IndexError):
            msg = f"HTTP {e.code}"
        if msg == "Private User":
            msg = f"{user}'s list is private"
        raise AniListError(400 if e.code < 500 else 502, f"AniList: {msg}") from None
    except (urllib.error.URLError, TimeoutError) as e:
        raise AniListError(502, f"Could not reach AniList: {getattr(e, 'reason', e)}") from None
    seen = {}
    for lst in data["data"]["MediaListCollection"]["lists"]:
        for e in lst["entries"]:     # an entry can sit in a status list and in custom lists too
            seen.setdefault(e["media"]["id"], e["status"])
    return list(seen.items())


def import_list(lib, user, statuses):
    """Creates (or tops up) the playlist "AniList · <user>"."""
    user = re.sub(r"^(https?://)?(www\.)?anilist\.co/user/", "", user.strip().rstrip("/"), flags=re.I).split("/")[0]
    if not re.fullmatch(r"[A-Za-z0-9_-]{2,40}", user):
        raise AniListError(400, "Enter an AniList user name or profile link")
    statuses = [s for s in statuses if s in STATUSES] or ["CURRENT", "COMPLETED", "REPEATING"]
    media = [mid for mid, status in entries(user) if status in statuses]
    anime = [wid for mid in media for wid in lib.by_anilist.get(mid, ())]
    anime.sort(key=lambda w: (lib.works[w]["year"] or 9999, lib.works[w]["name"].lower()))
    ids = [i for w in anime for i in lib.work_songs(w) if lib.songs[i]["file"] and not lib.songs[i]["dub"]]
    ids = lib.dedupe(ids)       # one version of each song (rebroadcasts, a song shared by two seasons)
    name = f"AniList · {user}"
    pid, created, added = userdata.fill_playlist(name, ids)
    found = sum(1 for mid in media if mid in lib.by_anilist)
    return {"id": pid, "name": name, "created": created, "entries": len(media), "found": found,
            "songs": len(ids), "added": added}
