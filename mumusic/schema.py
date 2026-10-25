"""Constants of the library file format (library.db), read by mumusic/library.py.

Ids carry their source: songs and works are "a<AMQ id>" / "e<EMQ id>", people are "a<AMQ artist>",
"g<AMQ group>" or "e<EMQ artist>". LIBRARY_FORMAT: newer library files say which app version they need.
"""

LIBRARY_FORMAT = 1

FLAG_DUB, FLAG_REBROADCAST, FLAG_INSTRUMENTAL = 1, 2, 4
TAG_GROUPS = ("genre", "mood", "vocals", "instruments", "tempo")
TYPE_ORDER = ("OP", "ED", "INS", "BGM")
