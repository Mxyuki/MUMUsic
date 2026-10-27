"""The song library in memory, and everything computed from it: search, pages, similar songs, recommendations
and discovery.

Everything is loaded for both sources, so likes and playlists always resolve and a liked AMQ song can steer
recommendations while only EMQ is in use. The active sources (AMQ, EMQ or both, set_sources) only limit what gets
picked: search results, similar songs, recommendations, discovery, tag pages. Song kinds the user turned off
(insert songs, BGM...) are left out of what the app picks by itself, but still found by search and on pages.

Sound vectors are whitened MuQ + CLAP embeddings, fitted on AMQ + EMQ together so songs of both libraries can be
compared. They stay int8 in memory (1.4 kB per song, as in the file) with one scale per
row, and are converted to float32 a block at a time: 4x faster than float16, a quarter of float32's memory.
"""
import json
import math
import random
import re
import sqlite3
from collections import defaultdict
from urllib.parse import quote, unquote

import numpy as np

from . import config
from .schema import FLAG_DUB, FLAG_INSTRUMENTAL, FLAG_REBROADCAST, TYPE_ORDER

# How much each tag group counts when comparing the "vibe" of two songs (on top of idf: rare tags weigh more).
VIBE_GROUP_WEIGHT = {"genre": 1.0, "mood": 0.8, "vocals": 0.5, "instruments": 0.5, "tempo": 0.4}
VERSION_RX = re.compile(r"\s*(\([^)]*\)|\[[^\]]*\]|～[^～]*～|~[^~]*~|-[^-]*(ver|version|mix|edit)\.?-)\s*", re.I)
ROLES = ("perf", "comp", "arr", "lyr")
ROLE_LABEL = {"perf": "Performed by", "comp": "Composed by", "arr": "Arranged by", "lyr": "Lyrics by"}
BLOCK = 2048


def base_title(name):
    """'unravel (Acoustic Version)', 'Connect -Orchestra ver.-' -> 'unravel', 'connect'."""
    return VERSION_RX.sub(" ", name).strip().lower() or name.lower()


def json_list(text):
    try:
        return json.loads(text) if text else []
    except ValueError:
        return []


def source_of(item_id):
    return "emq" if item_id.startswith("e") else "amq"


def song_kind(s):
    """"amq:OP", "emq:BGM"... (config.SONG_KINDS)."""
    return f"{s['src']}:{s['type'].split(' ')[0] or 'OTHER'}"


class Song:
    """One song's info. Slots instead of a dict (87k songs: ~25 MB instead of ~110), read like a dict."""
    __slots__ = ("id", "src", "songKey", "name", "artist", "workId", "work", "workKind", "year", "type", "dur", "vol",
                 "pop", "dub", "rebroadcast", "instrumental", "file", "videoFile", "perf", "comp", "arr", "lyr",
                 "search", "titleKey", "baseTitle", "tags")

    def __init__(self, **fields):
        for k, v in fields.items():
            setattr(self, k, v)

    def __getitem__(self, key):
        return getattr(self, key)

    def get(self, key, default=None):
        return getattr(self, key, default)


class Vectors:
    """Unit sound vectors stored as int8 rows + a float32 scale per row (1 / norm)."""

    def __init__(self, q):
        self.q = q
        self.scale = np.empty(len(q), dtype=np.float32)
        for start in range(0, len(q), BLOCK):
            self.scale[start:start + BLOCK] = 1 / np.linalg.norm(q[start:start + BLOCK].astype(np.float32), axis=1)

    def __len__(self):
        return len(self.q)

    def __getitem__(self, rows):
        """Float32 unit vector(s) of one row or a list of rows."""
        scale = self.scale[rows]
        return self.q[rows].astype(np.float32) * (scale[..., None] if np.ndim(scale) else scale)

    def dot(self, v):
        """Every row @ v (a vector or a matrix of column vectors), without a float32 copy of the whole matrix."""
        v = np.asarray(v, dtype=np.float32)
        out = np.empty((len(self.q),) + v.shape[1:], dtype=np.float32)
        buf = np.empty((BLOCK, self.q.shape[1]), dtype=np.float32)
        for start in range(0, len(self.q), BLOCK):
            block = buf[:len(self.q[start:start + BLOCK])]
            np.copyto(block, self.q[start:start + BLOCK], casting="unsafe")
            out[start:start + BLOCK] = block @ v
        return out * (self.scale[:, None] if v.ndim > 1 else self.scale)


class Library:
    def __init__(self, path, sources, skip_kinds=()):
        self.path = path
        db = sqlite3.connect(f"file:{path}?mode=ro", uri=True)
        db.row_factory = sqlite3.Row
        try:
            self.meta = dict(db.execute("SELECT key, value FROM meta"))
            self.load_info(db)
            self.load_sound(db)
        finally:
            db.close()
        self.set_sources(sources, skip_kinds)

    # ---------------------------------------------------------------- loading

    def load_info(self, db):
        self.works = {}
        for r in db.execute("SELECT * FROM works"):
            self.works[r["id"]] = {
                "id": r["id"], "kind": r["kind"], "name": r["name"], "nameJa": r["nameJa"], "year": r["year"],
                "category": r["category"], "genres": json_list(r["genres"]), "tags": json_list(r["tags"]),
                "score": r["score"], "aniListId": r["aniListId"], "vndbId": r["vndbId"],
                "developers": json_list(r["developers"]),
                "search": " ".join(filter(None, [r["name"], r["nameJa"], r["altNames"]])).lower(),
            }
        self.by_anilist = defaultdict(list)        # a few AniList entries map to 2-3 AMQ anime
        self.by_vndb = defaultdict(list)
        for w in self.works.values():
            if w["aniListId"]:
                self.by_anilist[w["aniListId"]].append(w["id"])
            if w["vndbId"]:
                self.by_vndb[w["vndbId"]].append(w["id"])

        self.people = {r["id"]: {"id": r["id"], "kind": r["kind"], "name": r["name"], "search": r["name"].lower()}
                       for r in db.execute("SELECT id, kind, name FROM people")}
        self.alias = defaultdict(set)              # person -> every id of the same person / group (itself included)
        roots = dict(db.execute("SELECT id, COALESCE(alias, id) FROM people"))
        clusters = defaultdict(set)
        for pid, root in roots.items():
            clusters[root].add(pid)
        for pid, root in roots.items():
            self.alias[pid] = clusters[root]
        self.parents = defaultdict(set)            # person -> groups it is a member of
        self.members = defaultdict(list)           # group -> member people
        for g, m in db.execute("SELECT groupId, memberId FROM members"):
            self.parents[m].add(g)
            self.members[g].append(m)

        # Tags as three flat arrays (song number, tag id, score) instead of a dict per tag: 5 MB instead of 110.
        # A song's tags are the slice songs[sid]["tags"] = (start, end).
        names = db.execute("SELECT id, tagGroup, name FROM tagNames").fetchall()
        size = max((r[0] for r in names), default=0) + 1
        self.tag_group, self.tag_name = [""] * size, [""] * size
        for tid, group, name in names:
            self.tag_group[tid], self.tag_name[tid] = group, name
        self.tag_id_of = {name: tid for tid, _, name in names}
        t_song, t_id, t_score = [], [], []
        self.songs, self.song_list, self.song_number, self.file_to_id = {}, [], {}, {}
        self.by_role = {role: defaultdict(list) for role in ROLES}
        self.by_work = defaultdict(list)
        cols = ("id", "workId", "songKey", "name", "artist", "type", "flags", "duration", "volume", "popularity",
                "audio", "video", "performer", "composer", "arranger", "lyricist", "tags")
        for r in db.execute(f"SELECT {', '.join(cols)} FROM songs"):
            sid, src = r["id"], source_of(r["id"])
            w = self.works.get(r["workId"], {})
            name, artist = r["name"], r["artist"]
            tags = json_list(r["tags"])
            s = Song(
                id=sid, src=src, songKey=r["songKey"], name=name, artist=artist, workId=r["workId"],
                work=w.get("name") or "", workKind=w.get("kind"), year=w.get("year"), type=r["type"] or "",
                dur=r["duration"], vol=r["volume"], pop=r["popularity"], dub=bool(r["flags"] & FLAG_DUB),
                rebroadcast=bool(r["flags"] & FLAG_REBROADCAST), instrumental=bool(r["flags"] & FLAG_INSTRUMENTAL),
                file=r["audio"], videoFile=r["video"], perf=r["performer"], comp=r["composer"], arr=r["arranger"],
                lyr=r["lyricist"], search=f"{name} {artist}".lower(), titleKey=f"{name.lower()}|{artist.lower()}",
                baseTitle=base_title(name), tags=(len(t_id), len(t_id) + len(tags)))
            t_song.extend([len(self.song_list)] * len(tags))
            t_id.extend(t for t, _ in tags)
            t_score.extend(score for _, score in tags)
            self.songs[sid] = s
            self.song_number[sid] = len(self.song_list)
            self.song_list.append(sid)
            self.by_work[s["workId"]].append(sid)
            for role in ROLES:
                if s[role]:
                    self.by_role[role][s[role]].append(sid)
            for f in (r["audio"], r["video"]):
                if f:
                    self.file_to_id[f] = sid
        self.t_song = np.array(t_song, dtype=np.int32)
        self.t_id = np.array(t_id, dtype=np.int16)
        self.t_score = np.array(t_score, dtype=np.float32)

        for p in self.people.values():
            cluster = self.alias[p["id"]]
            p["count"] = sum(len(self.by_role["perf"].get(i, ())) for i in cluster)
            p["credits"] = sum(len(self.by_role[role].get(i, ())) for i in cluster for role in ROLES)

    @staticmethod
    def urls(s):
        """(audio, video, public link). EMQ only serves its files with session cookies, so they go through the
        local /media/ proxy (server.py)."""
        def url(f):
            if not f:
                return None
            if s["src"] == "amq":
                return config.AMQ_MEDIA + f
            return f if f.startswith("http") else "/media/" + quote(f)
        link = s["file"] and (config.AMQ_MEDIA + s["file"] if s["src"] == "amq" else config.EMQ_MEDIA + s["file"])
        return url(s["file"]), url(s["videoFile"]), link

    def load_sound(self, db):
        dims = int(self.meta.get("vectorDims") or 0)
        ids = [r[0] for r in db.execute("SELECT songId FROM vectors ORDER BY rowid")]
        q = np.empty((len(ids), dims), dtype=np.int8)
        for n, (blob,) in enumerate(db.execute("SELECT v FROM vectors ORDER BY rowid")):
            q[n] = np.frombuffer(blob, dtype=np.int8)
        self.emb, self.emb_ids = Vectors(q), ids
        self.emb_index = {sid: n for n, sid in enumerate(ids)}
        feats = {r[0]: r[1:] for r in db.execute(
            "SELECT id, bpm, loudness, brightness, punch, musicKey FROM songs WHERE bpm IS NOT NULL")}
        self.feature_arrays(ids, feats)
        self.tag_vectors(ids)

    def set_sources(self, sources, skip_kinds=()):
        """Which libraries songs are picked from, minus the song kinds turned off. Instant: nothing is reloaded."""
        sources, skip_kinds = frozenset(sources), frozenset(skip_kinds)

        def picked(sid):
            s = self.songs[sid]
            return s["src"] in sources and song_kind(s) not in skip_kinds
        mask = np.array([picked(sid) for sid in self.emb_ids], dtype=bool)
        active_ids = [sid for sid, ok in zip(self.emb_ids, mask) if ok]
        song_active = np.array([picked(sid) for sid in self.song_list], dtype=bool)
        tag_active = song_active[self.t_song] if len(self.t_song) else np.zeros(0, dtype=bool)
        counts = np.bincount(self.t_id[tag_active], minlength=len(self.tag_name))
        vocab = sorted(self.tag_name[t] for t in np.flatnonzero(counts))
        self.sources, self.skip_kinds, self.mask, self.active_ids, self.tag_active, self.tag_count, self.vocab_active = (
            sources, skip_kinds, mask, active_ids, tag_active, counts, vocab)

    def kind_counts(self):
        """Songs of each kind ({"amq:OP": 8839, ...}), for the settings."""
        counts = defaultdict(int)
        for s in self.songs.values():
            counts[song_kind(s)] += 1
        return counts

    def only_active(self, scores):
        """Scores of songs outside the active sources pushed to the bottom."""
        return np.where(self.mask, scores, -np.inf)

    def active(self, sid):
        s = self.songs.get(sid)
        return s is not None and s["src"] in self.sources

    # ---------------------------------------------------------------- features & tag vectors

    def feature_arrays(self, ids, feats):
        """Measured features aligned with the vector rows, as z-scores (NaN when unknown): log2 BPM, loudness,
        log brightness, punch. Also the raw values and their sorted columns, for a song's percentiles."""
        none = (None,) * 5
        a = np.array([feats.get(sid, none)[:4] for sid in ids], dtype=np.float64).reshape(len(ids), 4)
        self.feat_raw = a.copy()
        self.music_key = {sid: f[4] for sid, f in feats.items()}
        with np.errstate(divide="ignore", invalid="ignore"):
            a[:, 0] = np.log2(a[:, 0])
            a[:, 2] = np.log(a[:, 2])
            z = (a[:, 1:] - np.nanmean(a[:, 1:], axis=0)) / (np.nanstd(a[:, 1:], axis=0) + 1e-9)
        self.feat_sorted = {k: np.sort(a[~np.isnan(a[:, n]), n])
                            for n, k in enumerate(("bpm", "loudness", "brightness", "punch"))}
        self.feat = {"logbpm": a[:, 0], "z": z}

    def feature_view(self, sid):
        row = self.emb_index.get(sid)
        if row is None or np.isnan(self.feat_raw[row, 0]):
            return None
        bpm, loudness, brightness, punch = self.feat_raw[row]

        def pct(k, v):
            arr = self.feat_sorted[k]
            return round(float(np.searchsorted(arr, v) / len(arr)), 3) if len(arr) and not np.isnan(v) else None
        return {"bpm": round(bpm), "key": self.music_key.get(sid), "loudness": pct("loudness", loudness),
                "brightness": pct("brightness", math.log(brightness)) if brightness > 0 else None,
                "punch": pct("punch", punch)}

    def feature_sim(self, rows, ref):
        """How close songs are in tempo (half / double time counts as close), loudness, brightness and punch."""
        lb = self.feat["logbpm"]
        if np.isnan(lb[ref]):
            return np.zeros(len(rows))
        d = np.abs(lb[rows] - lb[ref])
        d = np.minimum(d, np.abs(d - 1))
        s = -d / 0.08 - 0.5 * np.abs(self.feat["z"][rows] - self.feat["z"][ref]).sum(axis=1)
        return np.nan_to_num(s, nan=float(np.nanmedian(s)) if np.isfinite(s).any() else 0.0)

    def tag_vectors(self, ids):
        """idf-weighted tag vector per vector row: sharing a rare tag (jazz, metal) counts much more than a common
        one (j-pop)."""
        row_of_song = np.full(len(self.song_list), -1, dtype=np.int64)
        for n, sid in enumerate(ids):
            row_of_song[self.song_number[sid]] = n
        rows = row_of_song[self.t_song] if len(self.t_song) else np.zeros(0, dtype=np.int64)
        has = rows >= 0
        counts = np.bincount(self.t_id[has], minlength=len(self.tag_name))
        idf = np.log((len(ids) + 1) / (counts + 1))
        group_weight = np.array([VIBE_GROUP_WEIGHT.get(g, 0.5) for g in self.tag_group], dtype=np.float32)
        tid = self.t_id[has].astype(np.int64)
        tv = np.zeros((len(ids), len(self.tag_name)), dtype=np.float32)
        tv[rows[has], tid] = idf[tid] * group_weight[tid] * (0.5 + self.t_score[has])
        norm = np.linalg.norm(tv, axis=1, keepdims=True)
        self.tagvec = np.divide(tv, norm, out=tv, where=norm > 0)

    def song_tags(self, sid):
        start, end = self.songs[sid]["tags"]
        return [{"group": self.tag_group[t], "tag": self.tag_name[t], "score": round(float(score), 3)}
                for t, score in zip(self.t_id[start:end].tolist(), self.t_score[start:end].tolist())]

    # ---------------------------------------------------------------- song views

    def song_view(self, sid, extra=None):
        s = self.songs[sid]
        start, end = s["tags"]
        names = [(self.tag_group[t], self.tag_name[t]) for t in self.t_id[start:end].tolist()]
        short = [t for g, t in names if g == "genre"][:2] + [t for g, t in names if g == "mood"][:1]
        v = {k: s[k] for k in ("id", "src", "name", "artist", "work", "workId", "workKind", "year", "type", "dur",
                               "vol")}
        v["audio"], v["video"], v["link"] = self.urls(s)
        v["tags"] = short
        v["analysed"] = sid in self.emb_index
        v["by"] = s["perf"] or s["comp"]
        if extra:
            v.update(extra)
        return v

    def views(self, ids):
        return [self.song_view(i) for i in ids if i in self.songs]

    def dedupe(self, ids, skip_keys=(), skip_titles=()):
        """One version per song (prefer playable, original airing, analysed), in the order given."""
        best, order = {}, []
        for i in ids:
            s = self.songs[i]
            if s["songKey"] in skip_keys or s["titleKey"] in skip_titles:
                continue
            key = s["titleKey"]
            rank = (s["file"] is None, s["dub"], s["rebroadcast"], i not in self.emb_index)
            if key not in best:
                order.append(key)
                best[key] = (rank, i)
            elif rank < best[key][0]:
                best[key] = (rank, i)
        return [best[k][1] for k in order]

    def sort_popular(self, ids):
        s = self.songs
        return sorted(ids, key=lambda i: (s[i]["pop"] is None, s[i]["pop"] or 0, -(s[i]["year"] or 0)))

    def random_picks(self, n=16):
        return self.views(self.dedupe(random.sample(self.active_ids, min(n, len(self.active_ids)))))

    # ---------------------------------------------------------------- search

    def search(self, q, limit=80):
        words = [w for w in q.lower().split() if w]
        if not words:
            return {"songs": [], "people": [], "works": [], "tags": []}
        ql = " ".join(words)
        scored = []
        for s in self.songs.values():
            if s["src"] not in self.sources:
                continue
            hay = s["search"] + " " + self.works.get(s["workId"], {}).get("search", "")
            if not all(w in hay for w in words):
                continue
            name = s["name"].lower()
            score = 100 if name == ql else 60 if name.startswith(ql) else 35 if ql in name else 0
            score += 25 if ql in s["artist"].lower() else 0
            score += 3 if s["pop"] is not None and s["pop"] < 2000 else 0
            scored.append((-score, s["pop"] or 10 ** 6, s["id"]))
        scored.sort()
        song_ids = self.dedupe([i for _, _, i in scored])[:limit]

        hits = [p for p in self.people.values() if p["credits"] and source_of(p["id"]) in self.sources
                and all(w in p["search"] for w in words)]
        hits.sort(key=lambda p: (p["search"] != ql, not p["search"].startswith(ql), -p["credits"]))
        people, seen = [], set()
        for p in hits:
            cluster = frozenset(self.alias[p["id"]])
            if cluster not in seen:
                seen.add(cluster)
                people.append({"id": p["id"], "name": p["name"], "kind": p["kind"], "count": p["credits"]})

        works = [w for w in self.works.values() if source_of(w["id"]) in self.sources
                 and all(word in w["search"] for word in words)]
        works.sort(key=lambda w: (w["name"].lower() != ql, not w["name"].lower().startswith(ql), -(w["year"] or 0)))
        return {
            "songs": self.views(song_ids),
            "people": people[:12],
            "works": [{"id": w["id"], "kind": w["kind"], "name": w["name"], "year": w["year"]} for w in works[:12]],
            "tags": [t for t in self.vocab_active if ql in t][:10],
        }

    # ---------------------------------------------------------------- people, works, songs

    def name_of(self, pid):
        return self.people.get(pid, {}).get("name") or "?"

    def upward_groups(self, nodes):
        """Every group that (transitively) contains one of the given people. Group aliases are not followed:
        they are often other line-ups of the group."""
        seen, frontier = set(), list(nodes)
        while frontier:
            for g in self.parents.get(frontier.pop(), ()):
                if g not in seen:
                    seen.add(g)
                    frontier.append(g)
        return seen

    def person_page(self, pid):
        p = self.people.get(pid)
        if not p:
            return None
        own = self.alias[pid]
        via = self.upward_groups(own) - own

        def collect(role, people):
            ids = {i for x in people for i in self.by_role[role].get(x, ()) if self.active(i)}
            return self.views(self.dedupe(self.sort_popular(ids)))

        page = {
            "id": pid, "kind": p["kind"], "name": p["name"],
            "aliases": sorted({self.name_of(i) for i in own} - {p["name"]}),
            "groups": sorted(({"id": g, "name": self.name_of(g), "count": self.people.get(g, {}).get("count", 0)}
                              for g in {g for x in own for g in self.parents.get(x, ())}),
                             key=lambda x: -x["count"])[:40],
            "songs": collect("perf", own | via),
            "composed": collect("comp", own | via),
            "arranged": collect("arr", own | via),
            "lyrics": collect("lyr", own),
        }
        members = {m for g in own for m in self.members.get(g, ())}
        if members:
            page["members"] = sorted(({"id": m, "name": self.name_of(m)} for m in members),
                                     key=lambda x: x["name"].lower())
        return page

    def work_songs(self, wid):
        """OPs, then EDs, insert songs, BGM."""
        def key(i):
            s = self.songs[i]
            kind = s["type"].split(" ")[0]
            return TYPE_ORDER.index(kind) if kind in TYPE_ORDER else len(TYPE_ORDER), s["type"], s["name"].lower()
        return sorted(self.by_work.get(wid, ()), key=key)

    def work_page(self, wid):
        w = self.works.get(wid)
        if not w:
            return None
        fields = ("id", "kind", "name", "nameJa", "year", "category", "genres", "tags", "score", "vndbId", "developers")
        return {k: w[k] for k in fields} | {"songs": self.views(self.work_songs(wid))}

    def work_site(self, wid):
        """The anime's AniList page / the VN's VNDB page (a search there when the library has no id)."""
        w = self.works.get(wid)
        if not w:
            return None
        if w["kind"] == "vn":
            return f"{config.VNDB_SITE}/{w['vndbId']}" if w["vndbId"] else f"{config.VNDB_SITE}/v?q={quote(w['name'])}"
        if w["aniListId"]:
            return f"{config.ANILIST_SITE}/anime/{w['aniListId']}"
        return f"{config.ANILIST_SITE}/search/anime?search={quote(w['name'])}"

    def song_detail(self, sid):
        s = self.songs.get(sid)
        if not s:
            return None
        credits = [{"role": ROLE_LABEL[role], "id": s[role], "kind": self.people.get(s[role], {}).get("kind", "artist"),
                    "name": self.name_of(s[role])} for role in ROLES if s[role]]
        w = self.works.get(s["workId"], {})
        return self.song_view(sid) | {
            "fullTags": self.song_tags(sid), "credits": credits, "workGenres": w.get("genres", []),
            "workCategory": w.get("category"), "features": self.feature_view(sid), "file": s["file"],
        }

    def tag_page(self, tag):
        tid = self.tag_id_of.get(tag)
        pos = np.flatnonzero((self.t_id == tid) & self.tag_active) if tid is not None else np.zeros(0, dtype=np.int64)
        pos = pos[np.argsort(-self.t_score[pos], kind="stable")]
        ids = [(None, self.song_list[i]) for i in self.t_song[pos].tolist()]
        return {"tag": tag, "songs": self.views(self.dedupe([i for _, i in ids])[:400])}

    def tag_counts(self):
        tags = sorted(np.flatnonzero(self.tag_count).tolist(), key=lambda t: -self.tag_count[t])
        return [{"tag": self.tag_name[t], "group": self.tag_group[t], "count": int(self.tag_count[t])} for t in tags]

    def resolve(self, ref):
        """A song id, a song file name or a link to it (AMQ or EMQ) -> song id."""
        ref = ref.strip().split("?")[0].rstrip("/")
        if ref in self.songs:
            return ref
        for marker in ("/selfhoststorage/", "/media/"):
            if marker in ref:
                ref = ref.split(marker, 1)[1]
                break
        else:
            ref = ref.rsplit("/", 1)[-1]
        return self.file_to_id.get(unquote(ref))

    # ---------------------------------------------------------------- similarity

    def expanded(self, sid, n=3, weight=0.5):
        """Song vector nudged towards its 3 nearest songs from other works (query expansion): smooths out what is
        specific to this one recording. +9% same artist / composer in the top 10 in the AMQ benchmark."""
        v = self.emb[self.emb_index[sid]]
        sims = self.emb.dot(v)
        s = self.songs[sid]
        near = []
        for j in np.argsort(-sims)[:60]:
            o = self.songs[self.emb_ids[j]]
            if o["workId"] != s["workId"] and o["baseTitle"] != s["baseTitle"]:
                near.append(j)
                if len(near) == n:
                    break
        return v + weight * self.emb[near].mean(axis=0) if near else v

    def similar(self, sid, n=30):
        if sid not in self.emb_index:
            return []
        sims = self.emb.dot(self.expanded(sid)) / 1.5
        order = np.argsort(-self.only_active(sims))[: n * 4 + 20]
        s = self.songs[sid]
        ids = self.dedupe([self.emb_ids[i] for i in order], {s["songKey"]}, {s["titleKey"]})[:n]
        return [self.song_view(i, {"match": round(float(sims[self.emb_index[i]]), 3)}) for i in ids]

    def recommend(self, liked, n=60, seed=None):
        liked = [i for i in liked if i in self.emb_index]
        if not liked:
            return []
        L = self.emb[[self.emb_index[i] for i in liked]]
        sims = self.emb.dot(L.T)                                  # (songs, liked)
        k = min(3, len(liked))
        score = np.sort(sims, axis=1)[:, -k:].mean(axis=1)         # close to several liked songs, not the average
        because = sims.argmax(axis=1)
        if seed is not None:                                       # "refresh": small shuffle in the ranking
            score = score + np.random.default_rng(seed).normal(0, 0.012, len(score))
        order = np.argsort(-self.only_active(score))[: n * 8]
        skip_keys = {self.songs[i]["songKey"] for i in liked}
        skip_titles = {self.songs[i]["titleKey"] for i in liked}
        ids = self.dedupe([self.emb_ids[i] for i in order], skip_keys, skip_titles)
        out, per_artist, per_work = [], defaultdict(int), defaultdict(int)
        for i in ids:
            s = self.songs[i]
            if per_artist[s["artist"].lower()] >= 2 or per_work[s["workId"]] >= 2:
                continue
            per_artist[s["artist"].lower()] += 1
            per_work[s["workId"]] += 1
            j = self.emb_index[i]
            src = self.songs[liked[because[j]]]
            out.append(self.song_view(i, {"match": round(float(score[j]), 3),
                                          "because": {"id": src["id"], "name": src["name"]}}))
            if len(out) >= n:
                break
        return out

    def radio_next(self, seed, current, history, mode="close", skipped=(), liked=()):
        """Pick the next discovery song.

        close   (default) every pick is measured against the song the run started from: the unplayed songs
                that sound closest to it (MuQ + CLAP), share its genre / mood tags and have a similar tempo,
                loudness and punch. The best few are played first, so the run walks outwards from the seed.
        explore follows the current song more, so the run drifts further from where it started.
        Never repeats a song already played (or another version / cover of it), avoids recent artists & works.
        Learns within the run: away from songs the listener skipped quickly, towards songs they liked."""
        if seed not in self.emb_index:
            seed = current
        if current not in self.emb_index:
            current = seed
        if current not in self.emb_index:
            return None
        E, T = self.emb, self.tagvec
        si, ci = self.emb_index[seed], self.emb_index[current]
        seed_sim = E.dot(self.expanded(seed)) / 1.5
        if mode == "explore":
            pool = np.argsort(-self.only_active(0.7 * E.dot(E[ci]) + 0.3 * seed_sim))[:1500]
            # seed sound, current sound, seed tags, current tags, seed features, current features
            weights = (0.15, 0.40, 0.08, 0.22, 0.0, 0.10)
            top_k, temperature = 10, 0.3
        else:
            # the neighbourhood grows slowly with the run so long sessions don't run dry
            pool = np.argsort(-self.only_active(seed_sim))[:250 + 3 * len(history)]
            weights = (0.50, 0.10, 0.15, 0.05, 0.12, 0.03)
            top_k, temperature = 5, 0.15

        played = [self.songs[i] for i in history if i in self.songs] + [self.songs[current]]
        skip_keys = {s["songKey"] for s in played}
        skip_titles = {s["titleKey"] for s in played}
        skip_names = {s["baseTitle"] for s in played}                # covers, acoustic / orchestra versions
        recent_artists = defaultdict(int)                             # each recent play of an artist pushes it down
        for s in played[-8:]:
            recent_artists[s["artist"].lower()] += 1
        recent_works = {s["workId"] for s in played[-4:]}
        keep = []
        for j in pool:
            s = self.songs[self.emb_ids[j]]
            if (self.mask[j] and s["file"] and s["songKey"] not in skip_keys and s["titleKey"] not in skip_titles
                    and s["baseTitle"] not in skip_names):
                keep.append(j)
        if not keep:
            return None
        keep = np.array(keep)

        def z(x):
            return (x - x.mean()) / (x.std() + 1e-6)

        Ek = E[keep]
        parts = (seed_sim[keep], Ek @ E[ci], T[keep] @ T[si], T[keep] @ T[ci],
                 self.feature_sim(keep, si), self.feature_sim(keep, ci))
        score = sum(w * z(x) for w, x in zip(weights, parts) if w)
        # era: songs from around the same years tend to share production style
        seed_year = self.songs[seed]["year"]
        if seed_year:
            years = np.array([self.songs[self.emb_ids[j]]["year"] or seed_year for j in keep], dtype=np.float32)
            score += (0.08 if mode != "explore" else 0.04) * z(np.exp(-np.abs(years - seed_year) / 8))
        skipped = [self.emb_index[i] for i in skipped if i in self.emb_index][-12:]
        if skipped:
            score -= 0.35 * z((Ek @ E[skipped].T).max(axis=1))
        liked = [self.emb_index[i] for i in liked if i in self.emb_index][-12:]
        if liked:
            score += 0.25 * z((Ek @ E[liked].T).max(axis=1))
        for n, j in enumerate(keep):
            s = self.songs[self.emb_ids[j]]
            score[n] -= 0.45 * recent_artists.get(s["artist"].lower(), 0) + (0.3 if s["workId"] in recent_works else 0)
        top = np.argsort(-score)[:top_k]
        probs = np.exp((score[top] - score[top[0]]) / temperature)
        pick = self.emb_ids[keep[np.random.choice(top, p=probs / probs.sum())]]
        return self.song_view(pick, {"match": round(float(seed_sim[self.emb_index[pick]]), 3)})
