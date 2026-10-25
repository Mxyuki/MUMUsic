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

At each start MUMUsic checks for a new version of the app and of the library, and asks before updating.
Your likes, playlists and history stay on your computer (`%APPDATA%\MUMUsic`) and are kept across updates.

## Use

- **AMQ / EMQ / Both** (top of the sidebar): which library you browse, search and discover in.
- **Search** with `/`: songs, artists, anime, VNs, tags, or paste an AMQ / EMQ song link.
- **Discovery**: starts from a song (or your likes) and keeps going with close matches, never repeating.
  Skip a song in its first 30 seconds and discovery steers away from that sound; like songs to steer towards it.
- **For you**: songs that sound like several of your liked songs.
- Click the cover in the player bar for the big view with the anime clip.
- Space play / pause, Shift + arrows previous / next, arrows seek.
- Closing the window keeps the music playing in the tray; use the tray menu to quit.
- **Data** page: export / import your likes and playlists (also reads exports of the older AMQ / EMQ players).

## Run from source

```
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
MUMUsic.cmd
```

## License

The app is MIT licensed (see `LICENSE`). The fonts in `mumusic/web/fonts` are under the SIL Open Font License.
The song library itself is not part of this repository.
