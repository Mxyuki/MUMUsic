"""Discord status ("Listening to MUMUsic"): the playing song, its artist, anime / VN and type, with a progress bar.

Talks to the Discord app on this computer over its local pipe (\\\\.\\pipe\\discord-ipc-N, the protocol of Discord's
RPC SDK: 8-byte header = opcode + length, then JSON). Discord only shows a status attached to an application made
at discord.com/developers: its id is config.DISCORD_CLIENT_ID, or the one pasted on the Data page (settings), its
name is the "Listening to ..." line, config.DISCORD_IMAGE the picture.
Updates are coalesced: Discord accepts about 5 per 20 seconds, so a burst of seeks sends only the last state.
When Discord isn't running, it tries again every 20 seconds.
"""
import json
import os
import re
import struct
import threading
import time
import uuid

from . import config, settings

PIPES = "\\\\.\\pipe\\"
OP_HANDSHAKE, OP_FRAME = 0, 1
LISTENING = 2                     # activity type: "Listening to ..."
MIN_INTERVAL = 4.0                # seconds between two updates
RETRY = 20.0
APP_ID = re.compile(r"\d{17,20}")


class DiscordError(Exception):
    pass


def client_id():
    return settings.get("discordAppId") or config.DISCORD_CLIENT_ID


def open_pipe(app_id):
    """Connected pipe after a successful handshake. DiscordError says why not."""
    for n in range(10):
        try:
            pipe = open(f"{PIPES}discord-ipc-{n}", "r+b", buffering=0)
        except OSError:
            continue
        try:
            write(pipe, OP_HANDSHAKE, {"v": 1, "client_id": app_id})
            op, data = read(pipe)
        except (OSError, ValueError, struct.error):
            pipe.close()
            continue
        if op == OP_FRAME and data.get("evt") == "READY":
            return pipe
        pipe.close()
        raise DiscordError(f"Discord refused this application id ({data.get('message') or data})")
    raise DiscordError("Discord isn't running on this computer")


def write(pipe, op, payload):
    data = json.dumps(payload).encode("utf-8")
    pipe.write(struct.pack("<II", op, len(data)) + data)


def read(pipe):
    op, length = struct.unpack("<II", pipe.read(8))
    return op, json.loads(pipe.read(length) or b"{}")


def test(app_id):
    """Checks an application id against the running Discord (raises DiscordError)."""
    if not APP_ID.fullmatch(app_id or ""):
        raise DiscordError("An application id is a number of 17 to 20 digits")
    open_pipe(app_id).close()


class DiscordPresence:
    def __init__(self):
        self.pipe = self.pipe_id = None
        self.wanted = self.sent = None        # latest activity asked for / sent (None = no status)
        self.last_send = 0.0
        self.status = "idle"                  # shown on the Data page
        self.wake = threading.Event()
        threading.Thread(target=self.loop, daemon=True).start()

    @property
    def available(self):
        return bool(client_id())

    def update(self, song):
        """song: {name, artist, work, workKind, type, position, duration, playing} or None to clear."""
        self.wanted = self.activity(song) if song else None
        self.wake.set()

    def reset(self):
        """The application id changed: reconnect with it and send the current song again."""
        self.close()
        self.sent = None
        self.wake.set()

    @staticmethod
    def activity(song):
        kind = {"anime": "Anime", "vn": "VN"}.get(song.get("workKind"), "")
        about = " · ".join(x for x in (song.get("type"), kind) if x)
        a = {
            "type": LISTENING,
            "details": (song.get("name") or "?")[:128],
            "state": (song.get("artist") or "Unknown artist")[:128],
            "assets": {"large_image": config.DISCORD_IMAGE,
                       "large_text": (f"{song.get('work') or ''}" + (f" ({about})" if about else ""))[:128]},
        }
        if song.get("playing") and song.get("duration"):
            # a start and an end give the progress bar with the current time / the length
            start = time.time() - float(song.get("position") or 0)
            a["timestamps"] = {"start": int(start * 1000), "end": int((start + float(song["duration"])) * 1000)}
        else:
            a["state"] = f"Paused · {a['state']}"[:128]
        return a

    def close(self):
        if self.pipe:
            try:
                self.pipe.close()
            except OSError:
                pass
        self.pipe = None

    def send(self, activity):
        write(self.pipe, OP_FRAME, {"cmd": "SET_ACTIVITY", "nonce": str(uuid.uuid4()),
                                    "args": {"pid": os.getpid(), "activity": activity}})
        _, answer = read(self.pipe)
        if answer.get("evt") == "ERROR":
            raise DiscordError(f"Discord: {answer.get('data', {}).get('message')}")

    def loop(self):
        while True:
            self.wake.wait(RETRY)
            self.wake.clear()
            app_id = client_id()
            if not app_id or self.wanted == self.sent:
                continue
            wait = self.last_send + MIN_INTERVAL - time.time()
            if wait > 0:
                time.sleep(wait)
            activity = self.wanted
            try:
                if self.pipe is None or self.pipe_id != app_id:
                    self.close()
                    self.pipe, self.pipe_id = open_pipe(app_id), app_id
                self.send(activity)
                self.sent, self.last_send, self.status = activity, time.time(), "connected"
            except DiscordError as e:
                self.status = str(e)
                self.close()
            except (OSError, ValueError, struct.error):
                self.status = "Discord was closed"
                self.close()          # reconnect on the next update / retry


PRESENCE = DiscordPresence()
