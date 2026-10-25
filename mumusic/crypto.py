"""Encrypted library files (.mumu) and local protection of the access key.

A .mumu file (integers are big-endian):

    b"MUMU" | format version (1 byte) | salt (16) | key check (34) | nonce prefix (8) | chunk | chunk | ...
    chunk = length (4) | AES-256-GCM(zlib(next 4 MB of the plain file))

The key is scrypt(access key, salt); the key check (see key_check) tells a wrong key from a damaged file. Each chunk's nonce is the prefix + the chunk number, and the last chunk is
authenticated with the associated data b"end", so a reordered or truncated file fails to decrypt.
The encryption only keeps the library from being readable by anyone who finds the file; it is not meant to
resist a determined attacker (the access key is shared with every user).
"""
import base64
import hashlib
import os
import struct
import sys
import zlib

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

MAGIC = b"MUMU"
FORMAT = 1
CHUNK = 4 << 20
CHECK_TEXT = b"MUMUsic access key"
CHECK_SIZE = len(CHECK_TEXT) + 16
HEADER = len(MAGIC) + 1 + 16 + CHECK_SIZE + 8


class WrongKey(Exception):
    pass


def derive_key(passphrase, salt):
    return hashlib.scrypt(passphrase.encode("utf-8"), salt=salt, n=2 ** 15, r=8, p=1, maxmem=64 << 20, dklen=32)


def check_bytes(key):
    """Token that tells whether a key is right without decrypting the library (in the file header and, base64,
    in the manifest). A fixed nonce is fine: this is the only message encrypted with nonce 0 under this key."""
    return AESGCM(key).encrypt(bytes(12), CHECK_TEXT, None)


def key_check(key):
    return base64.b64encode(check_bytes(key)).decode()


def is_right_key(passphrase, salt_b64, check):
    key = derive_key(passphrase, base64.b64decode(salt_b64))
    return key_check(key) == check


def decrypt_file(src, dst, passphrase, progress=None):
    """Writes the plain file to dst (through a temporary file). Raises WrongKey when the key or the file is bad.
    `progress(fraction)` is called after each chunk."""
    size = os.path.getsize(src)
    tmp = f"{dst}.tmp"
    try:
        with open(src, "rb") as fin, open(tmp, "wb") as fout:
            head = fin.read(HEADER)
            if len(head) < HEADER or head[:4] != MAGIC or head[4] != FORMAT:
                raise WrongKey("This is not a MUMUsic library file")
            salt, check, prefix = head[5:21], head[21:21 + CHECK_SIZE], head[21 + CHECK_SIZE:]
            key = derive_key(passphrase, salt)
            if check_bytes(key) != check:
                raise WrongKey("Wrong access key")
            aes = AESGCM(key)
            index = 0
            while True:
                length = fin.read(4)
                if len(length) < 4:
                    raise WrongKey("The library file is incomplete")
                sealed = fin.read(struct.unpack(">I", length)[0])
                nonce = prefix + struct.pack(">I", index)
                last = fin.tell() >= size
                try:
                    plain = aes.decrypt(nonce, sealed, b"end" if last else b"")
                except InvalidTag:
                    raise WrongKey("The library file is damaged") from None
                fout.write(zlib.decompress(plain))
                index += 1
                if progress:
                    progress(fin.tell() / size)
                if last:
                    break
        os.replace(tmp, dst)
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)


# ---------------------------------------------------------------- local key storage

def protect(text):
    """Text -> string safe to store in settings.json. Windows: DPAPI (only this Windows user can read it back)."""
    data = text.encode("utf-8")
    if sys.platform == "win32":
        data = _dpapi(data, encrypt=True)
    return base64.b64encode(data).decode()


def unprotect(stored):
    try:
        data = base64.b64decode(stored)
        if sys.platform == "win32":
            data = _dpapi(data, encrypt=False)
        return data.decode("utf-8")
    except (ValueError, OSError):
        return None


def _dpapi(data, encrypt):
    import ctypes
    from ctypes import wintypes

    class Blob(ctypes.Structure):
        _fields_ = [("cbData", wintypes.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]

    crypt32, kernel32 = ctypes.windll.crypt32, ctypes.windll.kernel32
    src = Blob(len(data), ctypes.cast(ctypes.create_string_buffer(data, len(data)), ctypes.POINTER(ctypes.c_char)))
    out = Blob()
    fn = crypt32.CryptProtectData if encrypt else crypt32.CryptUnprotectData
    if not fn(ctypes.byref(src), None, None, None, None, 0x1, ctypes.byref(out)):   # 0x1 = no UI
        raise OSError("DPAPI failed")
    try:
        return ctypes.string_at(out.pbData, out.cbData)
    finally:
        kernel32.LocalFree(out.pbData)
