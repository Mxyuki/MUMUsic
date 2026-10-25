"""User settings (settings.json in the user folder): active sources, access key, window size, update choices."""
import json
import threading

from . import config

DEFAULTS = {
    "sources": list(config.SOURCES),
    "key": None,                 # access key, protected with crypto.protect
    "window": None,              # [width, height]
    "port": None,                # local server port, kept so the window's storage (theme, volume...) persists
    "libraryVersion": None,      # version of the installed library (from its manifest)
    "skipAppVersion": None,      # "skip this version" answer to an app update
    "discord": True,             # show the playing song as Discord status
    "discordAppId": None,        # Discord application id pasted on the Data page (else config.DISCORD_CLIENT_ID)
}

_lock = threading.Lock()


def load():
    try:
        data = json.loads(config.SETTINGS_FILE.read_text(encoding="utf-8"))
    except (FileNotFoundError, ValueError):
        data = {}
    return {**DEFAULTS, **{k: v for k, v in data.items() if k in DEFAULTS}}


def get(key):
    return load()[key]


def update(**values):
    with _lock:
        data = load() | values
        config.USER_DIR.mkdir(parents=True, exist_ok=True)
        tmp = config.SETTINGS_FILE.with_suffix(".tmp")
        tmp.write_text(json.dumps(data, indent=2), encoding="utf-8")
        tmp.replace(config.SETTINGS_FILE)
    return data
