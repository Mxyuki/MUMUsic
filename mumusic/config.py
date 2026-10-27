"""Paths, URLs and constants. Nothing else in the app hard-codes a path or a host."""
import os
import sys
from pathlib import Path

from . import __version__

APP_NAME = "MUMUsic"

# "owner/repository" on GitHub. Update checks (app and library) are off while it is empty.
GITHUB_REPO = "Mxyuki/MUMUsic"
LIBRARY_RELEASE = "library"            # tag of the release holding the encrypted library
LIBRARY_ASSET = "library.mumu"
MANIFEST_ASSET = "manifest.json"
SETUP_PREFIX = "MUMUsic-Setup-"        # the one asset of an app release: MUMUsic-Setup-<version>.exe

# Application id from discord.com/developers (public, not a secret). Discord status is off while it is empty.
DISCORD_CLIENT_ID = "1563715346635038792"
# The status picture: the application's art asset by URL. Discord caches an app's asset list in each client, so
# an asset name only works once that cache has caught up; a URL works right away.
DISCORD_IMAGE = "https://cdn.discordapp.com/app-assets/1563715346635038792/1563717609092808775.png"

FROZEN = getattr(sys, "frozen", False)
PACKAGE_DIR = Path(__file__).resolve().parent
WEB_DIR = PACKAGE_DIR / "web"
ICON_ICO = WEB_DIR / "icon.ico"
ICON_PNG = WEB_DIR / "icon.png"
INSTALL_DIR = Path(sys.executable).resolve().parent if FROZEN else PACKAGE_DIR.parent

USER_DIR = Path(os.environ.get("APPDATA") or Path.home() / ".config") / APP_NAME
SETTINGS_FILE = USER_DIR / "settings.json"
INSTANCE_FILE = USER_DIR / "instance.json"
USER_DB = USER_DIR / "user.db"
LIBRARY_DB = USER_DIR / "library.db"
DOWNLOAD_DIR = USER_DIR / "downloads"
# A plain library next to the source tree (the maintainer's own build): used as is, no key, no download.
DEV_LIBRARY_DB = PACKAGE_DIR.parent / "data" / "library.db"

SOURCES = ("amq", "emq")
SOURCE_PREFIX = {"amq": "a", "emq": "e"}      # song / work ids are "a<AMQ id>" and "e<EMQ id>"
# Kinds of song the user can turn off ("amq:INS"): the first word of a song's type, OTHER for EMQ songs without one
SONG_KINDS = {"amq": ("OP", "ED", "INS"), "emq": ("OP", "ED", "INS", "BGM", "OTHER")}

AMQ_MEDIA = "https://eudist.animemusicquiz.com/"
EMQ_SITE = "https://erogemusicquiz.com"
EMQ_MEDIA = EMQ_SITE + "/selfhoststorage/"
ANILIST_SITE = "https://anilist.co"
VNDB_SITE = "https://vndb.org"
VNDB_API = "https://api.vndb.org/kana"
USER_AGENT = f"{APP_NAME}/{__version__}"
