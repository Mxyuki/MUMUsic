"""EMQ song files, fetched with a guest session.

erogemusicquiz.com only serves its files (/selfhoststorage/...) to requests carrying the session cookies
`user-id` + `session-token` (see AuthController in github.com/mertvn/EMQ), and answers 500 without them. A browser
can't add those to a cross-site <audio> request, so the app's local server streams EMQ files through here.
The guest session is saved in the user folder and reused, so starting the app doesn't create a new guest each time.
"""
import json
import threading

import requests

from . import config

SESSION_FILE = config.USER_DIR / "emq-session.json"
RETRY_STATUSES = (401, 403, 500)       # an expired session gets 500 too


class EmqMedia:
    def __init__(self):
        self.lock = threading.Lock()
        self.local = threading.local()
        try:
            self.cookies = json.loads(SESSION_FILE.read_text(encoding="utf-8"))
        except (FileNotFoundError, ValueError):
            self.cookies = None

    def new_session(self):
        r = requests.post(f"{config.EMQ_SITE}/Auth/CreateSession", timeout=30, headers={"User-Agent": config.USER_AGENT},
                          json={"usernameOrEmail": "Guest", "password": "", "isGuest": True})
        r.raise_for_status()
        session = r.json()["session"]
        self.cookies = {"user-id": str(session["player"]["id"]), "session-token": session["token"]}
        config.USER_DIR.mkdir(parents=True, exist_ok=True)
        SESSION_FILE.write_text(json.dumps(self.cookies), encoding="utf-8")

    def http(self):
        if getattr(self.local, "cookies", None) != self.cookies:
            s = requests.Session()
            s.headers["User-Agent"] = config.USER_AGENT
            s.cookies.update(self.cookies)
            self.local.session, self.local.cookies = s, self.cookies
        return self.local.session

    def get(self, file_name, headers):
        """Streaming response for one file (Range header passed through, so seeking works)."""
        url = file_name if file_name.startswith("http") else config.EMQ_MEDIA + file_name
        used = None
        for attempt in range(2):
            with self.lock:
                # on a retry, renew only if no other request has renewed it meanwhile
                if self.cookies is None or (attempt and self.cookies is used):
                    self.new_session()
                used = self.cookies
            r = self.http().get(url, headers=headers, stream=True, timeout=60)
            if r.status_code in RETRY_STATUSES and attempt == 0:
                r.close()
                continue
            return r
