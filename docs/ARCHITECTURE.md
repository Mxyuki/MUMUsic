# Architecture

## Stack

| Need | Choice | Why |
|---|---|---|
| Window | .NET 8 WinForms + WebView2 | WebView2 comes with Windows 10 / 11. A native host gives the tray, snapping and single instance without workarounds. |
| UI | Plain HTML / CSS / JS in `web/` | No build step. The page talks to the host through `/api/*` and a few window messages. |
| Local server | A small HTTP/1.1 server on 127.0.0.1 (`Server/LocalServer.cs`) | The UI's origin stays `http://127.0.0.1:<port>`, so its storage (theme, volume, queue) survives restarts and updates. No http.sys reservation needed. |
| Library | SQLite (`Microsoft.Data.Sqlite`), held in memory | Sound vectors stay int8 (1.4 kB per song) and are widened to float with SIMD while comparing. |
| Setup | Inno Setup, per-user | No admin rights, so updates never show a UAC prompt. Uninstall entry, shortcuts and closing the app are Inno's job. |
| Secrets | DPAPI (`ProtectedData`) | The access key is stored per Windows user. |

## Layout

```
src/MUMUsic.Desktop/
  Program.cs              single instance (mutex + show / quit events), starts the server and the window, runs the update setup after exit
  MainForm.cs             the window: WebView2, window controls called by the page, tray, size saved on exit
  AppInfo.cs, AppPaths.cs names, URLs, version / user folder, program folder
  Windowing/              frameless chrome that keeps Windows' frame (snap, shadow), tray icon, page <-> host messages
  Server/                 LocalServer (HTTP), Api (routes, EMQ media proxy, static files), AppState (library loading)
  Library/                MusicLibrary: loading, search, pages (MusicLibrary.cs); vectors, similar, recommend, discovery (.Similarity.cs)
  UserData/               user.db: likes, playlists, history, backups, export / import
  Updates/                GitHub release checks, downloads, library install, the update job the UI polls
  Security/               scrypt, .mumu decryption, DPAPI key storage
  Settings/               settings.json
  Integrations/           EMQ guest session, Discord status, AniList / VNDB list imports, anime / VN pictures (WorkArt)
  Audio/                  plays the page's music from the app's own process (PageAudio over a WebSocket, waveOut)
  AppLinks.cs             mumusic://play/<song> links: parsing, hand-off to the running app over a named pipe
web/                      the UI
installer/MUMUsic.iss     the setup
scripts/                  build.ps1 (build + tests), release.ps1 (setup), make-installer-art.ps1
tests/                    xUnit: crypto against a file written by the publishing tools, versions, library helpers
```

Dependency direction: `MainForm / Program -> Server -> Library / UserData / Updates / Integrations / Audio -> Security / Settings / AppInfo`.
The library knows nothing about HTTP; the server holds no song logic.

## Page <-> host

- `/api/*` on the local server: everything about songs, user data, settings and updates. Requests from another
  origin or host name are refused.
- `/media/<file>`: EMQ files. EMQ only serves them with a session's cookies, which an `<audio>` element can't send
  cross-site, so the server streams them with a saved guest session (Range passed through for seeking).
- `ws://.../api/audio?rate=<Hz>`: the page's final mix (after volume levelling) as 16-bit stereo PCM, played by the
  app. WebView2 renders sound in msedgewebview2.exe, which Discord doesn't capture when the app window is streamed;
  sound from MUMUsic.exe is. The page is silent while the socket is open and plays by itself when it is not.
- WebView2 messages (`Windowing/HostBridge.cs`, the "window" section of `web/app.js`): `minimize`, `toggle_maximize`,
  `is_maximized`, `close` (hide to tray), `quit`, `start_move`, `start_resize`, `pick_library_file`, `open_url`,
  `take_open`. Events to the page: `windowState`, `tray`, `open`.

## Pictures and Discord links

- `GET /api/work/<id>/art`: the AniList cover of an anime or the VNDB cover of a VN (VNDB has no banners), found once
  and kept in `art-cache.json`. The big view shows it when a song has no clip, or when its clip fails to load.
  It also says whether the work is 18+ (a VNDB release rated 18+, or AniList's adult flag). Nothing is blurred by
  default; the "Adult works" option blurs clips and pictures of 18+ works, and B blurs / shows a clip (that song) or
  a picture (every song of that work), a choice that beats the option.
- The Discord status has a "Listen on MUMUsic" button for the people who see it. Discord buttons only open https
  links, so it opens `docs/play.html` (GitHub Pages), which opens `mumusic://play/<song>`. The setup registers that
  scheme; Windows starts MUMUsic with the link, or the running app gets it from the second start (`AppLinks.cs`). The
  WebView runs with autoplay allowed, so the song plays without a click.

## Install and update

```
first install     MUMUsic-Setup-<v>.exe        %LOCALAPPDATA%\Programs\MUMUsic, shortcuts, Settings > Apps entry
app start         GET /api/updates             newest v* release on GitHub with a MUMUsic-Setup-*.exe asset
"Update now"      POST /api/updates/app        user.db backup, download (sha256 from GitHub), app quits,
                                               Program.cs releases the mutex and runs the setup /SILENT
setup             replaces the program files, starts MUMUsic again ([Run] without skipifsilent)
next start        downloads\MUMUsic-Setup-*.exe deleted
```

- The setup and the uninstaller ask a running MUMUsic to quit (named event `MUMUsic.Quit`) and wait for its mutex
  to go, so the user never has to find the tray icon.
- The library is a separate release (`library`: `manifest.json` + `library.mumu`), updated in place. A library whose
  manifest asks for a newer app (`minApp`, `format`) is not offered until the app is updated.
- 1.x installs (Python) update themselves by running the newest setup with `--silent --launch`. The setup starts
  itself again with `/SILENT`, removes the 1.x files and its Settings > Apps entry, and keeps the same folder.
- Every setup writes a log to `%TEMP%\Setup Log <date>.txt`. The app logs to `%APPDATA%\MUMUsic\mumusic.log`.

## User folder (`%APPDATA%\MUMUsic`)

`user.db` (likes, playlists, plays; backed up to `user.backup.db` at each start and to `backups\` before each update),
`settings.json`, `library.db` (decrypted library), `webview\` (page storage), `emq-session.json`, `mumusic.log`.
Updates never touch it. The uninstaller asks before deleting it.
