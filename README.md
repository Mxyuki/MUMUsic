# MUMUsic

A desktop player for anime songs ([AMQ](https://animemusicquiz.com)) and eroge / visual novel songs
([EMQ](https://erogemusicquiz.com)), with search, likes, playlists and a discovery mode that keeps playing songs that
sound like the one you started from.

Every song in the library has been listened to by an audio model: songs get genre / mood / vocals / instruments /
tempo tags, a measured BPM and key, and a "sound fingerprint" used to find songs that sound alike, across anime,
visual novels and artists.

## Install

1. Download `MUMUsic-Setup-<version>.exe` from the [latest release](../../releases) and run it. It installs
   MUMUsic for your Windows account (no admin rights needed), with a Start menu and a desktop shortcut.
2. On the first start, enter the access key you were given. The song library (about 130 MB) downloads once.

To uninstall: Settings > Apps > MUMUsic. You can keep your likes and playlists for a later install.

Needs Windows 10 / 11 (Edge WebView2, already installed on both) and an internet connection: songs stream from
animemusicquiz.com and erogemusicquiz.com.

At each start MUMUsic checks for a new version of the app and of the library. Press **Update now**: the app closes,
installs the new version and opens again on its own. Your likes, playlists and history stay on your computer
(`%APPDATA%\MUMUsic`) and are kept across updates.

## Use

- **AMQ / EMQ / Both** (top of the sidebar): which library you browse, search and discover in.
- **Search** with `/`: songs, artists, anime, VNs, tags, or paste an AMQ / EMQ song link.
- **Discovery**: starts from a song (or your likes) and keeps going with close matches, never repeating.
  Skip a song in its first 30 seconds and discovery steers away from that sound; like songs to steer towards it.
- **For you**: songs that sound like several of your liked songs.
- Click the cover in the player bar for the big view with the anime clip. Songs without a clip show the anime's
  (AniList) or visual novel's (VNDB) picture instead; **B** blurs the clip or the picture. The Options page can blur
  every 18+ visual novel or adult anime automatically.
- With Discord open, your status shows the song you're listening to. Friends who have MUMUsic can press
  **Listen on MUMUsic** on it to play the same song.
- Space play / pause, Shift + arrows previous / next, arrows seek.
- Closing the window keeps the music playing in the tray; use the tray menu to quit.
- Playlists, liked songs and artists can show only some kinds of song (openings, endings, inserts, BGM) and only
  anime or VN songs; Play and Shuffle then play just those. An artist's songs are listed by anime / VN, then OP 1,
  OP 2..., ED 1, ED 2..., inserts, BGM.
- **Options** page: export / import your likes and playlists (also reads exports of the older AMQ / EMQ players).

## Build from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
scripts\build.ps1 -Run        tests, build, start the app (-Debug adds the web inspector)
scripts\release.ps1           dist\MUMUsic-Setup-<version>.exe (Inno Setup is downloaded the first time)
```

| Path | What |
|---|---|
| `src/MUMUsic.Desktop/` | The app: a WinForms window with WebView2, a local server for the UI, the library and updates |
| `web/` | The UI (plain HTML / CSS / JS) |
| `installer/` | The Inno Setup script of the setup, and its images |
| `tests/` | Tests (`dotnet test`) |

See `docs/ARCHITECTURE.md` for how it fits together.

## License

The app is MIT licensed (see `LICENSE`). The fonts in `web/fonts` are under the SIL Open Font License.
The song library itself is not part of this repository.
