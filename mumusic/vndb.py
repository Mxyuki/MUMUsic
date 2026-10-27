"""Playlist from a VNDB visual novel list: every song of every VN on it (EMQ songs only)."""
import re

import requests

from . import config, userdata

# VNDB's built-in list labels (api.vndb.org/kana, "ulist_labels"); 6 = Blacklist is never offered.
LABELS = {1: "Playing", 2: "Finished", 3: "Stalled", 4: "Dropped", 5: "Wishlist", 7: "Voted"}
DEFAULT_LABELS = (1, 2)
PAGE = 100                         # the API's maximum page size


class VndbError(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def call(method, path, **kwargs):
    # requests uses certifi's CA bundle: Python's own TLS fails on VNDB with an expired root in the Windows store
    try:
        r = requests.request(method, f"{config.VNDB_API}/{path}", timeout=30,
                             headers={"User-Agent": config.USER_AGENT}, **kwargs)
    except requests.RequestException as e:
        raise VndbError(502, f"Could not reach VNDB: {e.__class__.__name__}") from None
    if r.status_code == 429:
        raise VndbError(502, "VNDB: too many requests, try again in a few minutes")
    if not r.ok:
        raise VndbError(400 if r.status_code < 500 else 502, f"VNDB: {r.text.strip()[:200] or f'HTTP {r.status_code}'}")
    return r.json()


def find_user(text):
    """(user id "u123", user name) from a name, a user id or a profile / list link."""
    text = text.strip().rstrip("/")
    m = re.search(r"vndb\.org/(u\d+)", text, flags=re.I)
    query = m.group(1) if m else text
    if not re.fullmatch(r"[A-Za-z0-9_-]{2,40}", query):
        raise VndbError(400, "Enter a VNDB user name or profile link")
    user = call("GET", "user", params={"q": query}).get(query)
    if not user:
        raise VndbError(400, f"VNDB: no user named {query}")
    return user["id"], user["username"]


def entries(uid):
    """[(VN id "v17", label ids)] of the publicly visible part of a list."""
    out, page = [], 1
    while True:
        data = call("POST", "ulist", json={"user": uid, "fields": "labels.id", "sort": "id",
                                            "results": PAGE, "page": page})
        out += [(e["id"], {lb["id"] for lb in e["labels"]}) for e in data["results"]]
        if not data["more"]:
            return out
        page += 1


def import_list(lib, user, labels, bgm=True):
    """Creates (or tops up) the playlist "VNDB · <user>"."""
    labels = {int(lb) for lb in labels if str(lb).isdigit() and int(lb) in LABELS} or set(DEFAULT_LABELS)
    uid, name = find_user(user)
    vns = [vid for vid, on in entries(uid) if on & labels]
    works = [wid for vid in vns for wid in lib.by_vndb.get(vid, ())]
    works.sort(key=lambda w: (lib.works[w]["year"] or 9999, lib.works[w]["name"].lower()))
    ids = [i for w in works for i in lib.work_songs(w)
           if lib.songs[i]["file"] and (bgm or not lib.songs[i]["type"].startswith("BGM"))]
    ids = lib.dedupe(ids)
    title = f"VNDB · {name}"
    pid, created, added = userdata.fill_playlist(title, ids)
    found = sum(1 for vid in vns if vid in lib.by_vndb)
    return {"id": pid, "name": title, "created": created, "entries": len(vns), "found": found,
            "songs": len(ids), "added": added}
