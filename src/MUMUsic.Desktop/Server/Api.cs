using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MUMUsic.Desktop.Integrations;
using MUMUsic.Desktop.Library;
using MUMUsic.Desktop.Security;
using MUMUsic.Desktop.Settings;
using MUMUsic.Desktop.Updates;
using MUMUsic.Desktop.UserData;

namespace MUMUsic.Desktop.Server;

/// <summary>An error answered to the UI as {"error": message} with this status.</summary>
internal sealed class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>What the API needs from the window.</summary>
internal interface IWindowHost
{
    /// <summary>Quits the app; once it has quit, Program.cs runs this setup.</summary>
    void QuitAndInstall(string setupPath);
}

/// <summary>
/// The local server's routes: the JSON API under /api/, EMQ files under /media/, the UI's files for everything else.
/// The same routes and answers as the 1.x app, so web/app.js is unchanged.
/// </summary>
internal sealed partial class Api
{
    private static readonly Dictionary<string, string> StaticTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".css"] = "text/css; charset=utf-8", [".js"] = "text/javascript; charset=utf-8",
        [".svg"] = "image/svg+xml", [".png"] = "image/png", [".ico"] = "image/x-icon", [".woff2"] = "font/woff2",
        [".txt"] = "text/plain; charset=utf-8", [".json"] = "application/json; charset=utf-8",
    };
    private static readonly string[] MediaHeaders = ["Content-Type", "Content-Length", "Content-Range", "Content-Encoding", "Accept-Ranges", "Last-Modified", "ETag"];

    private readonly SettingsStore _settings;
    private readonly UserStore _user;
    private readonly UpdateService _updates;
    private readonly DiscordPresence _discord;
    private readonly ListImports _imports;
    private readonly EmqMedia _media = new();
    private readonly WorkArt _art = new();
    private readonly AppState _state;
    private readonly IWindowHost _window;
    private readonly List<(string Method, Regex Pattern, Func<HttpRequest, string[], Task<object?>> Handler)> _routes = [];
    private UpdateCheck? _updateCheck;
    private string _origin = "";

    public Api(SettingsStore settings, UserStore user, AppState state, IWindowHost window)
    {
        _settings = settings;
        _user = user;
        _state = state;
        _window = window;
        _updates = new UpdateService(settings, user);
        _discord = new DiscordPresence(settings);
        _imports = new ListImports(user);
        Register();
    }

    /// <summary>"http://127.0.0.1:port": requests from any other page are refused, so a website can't drive the API.</summary>
    public string Origin
    {
        set => _origin = value;
    }

    private MusicLibrary Lib => _state.Library ?? throw new ApiException(503, "The library is still loading");

    private static void Need(bool condition, int status = 404, string message = "Not found")
    {
        if (!condition) throw new ApiException(status, message);
    }

    private void Route(string method, string pattern, Func<HttpRequest, string[], Task<object?>> handler) =>
        _routes.Add((method, new Regex("^" + pattern + "$", RegexOptions.Compiled), handler));

    private void Route(string method, string pattern, Func<HttpRequest, string[], object?> handler) =>
        Route(method, pattern, (request, args) => Task.FromResult(handler(request, args)));

    private const string SongId = "([ae][0-9]+)";
    private const string PersonId = "([age][0-9]+)";

    private static int IntQuery(HttpRequest request, string key, int fallback) =>
        int.TryParse(request.Query.GetValueOrDefault(key), out var value) ? value : fallback;

    private void Register()
    {
        Route("GET", "/api/app", (_, _) => AppInfoView());
        Route("POST", "/api/setup", (request, _) => SetupAsync(request));
        Route("GET", "/api/job", (_, _) => With(_updates.Job.State(), "status", _state.Status));
        Route("GET", "/api/updates", (request, _) => UpdatesAsync(request.Query.ContainsKey("refresh")));
        Route("POST", "/api/updates/library", (_, _) => UpdateLibrary());
        Route("POST", "/api/updates/key", (request, _) => UpdateKey(request));
        Route("POST", "/api/updates/app", (_, _) => UpdateApp());
        Route("POST", "/api/updates/skip", (request, _) =>
        {
            var version = request.Json().Str("version");
            _settings.Update(s => s.SkipAppVersion = version);
            if (_updateCheck is not null) _updateCheck.App = null;
            return Ok;
        });
        Route("PUT", "/api/sources", (request, _) => SetSources(request));
        Route("PUT", "/api/song-kinds", (request, _) => SetSongKinds(request));
        Route("POST", "/api/presence", (request, _) =>
        {
            // The player reports what plays (or null) on every play / pause / seek / song change.
            var song = request.Json()["song"] as JsonObject;
            _discord.Update(song is not null && _settings.Current.Discord ? song : null);
            return Ok;
        });
        Route("PUT", "/api/discord", (request, _) => SetDiscord(request));

        Route("GET", "/api/home", (_, _) => Home());
        Route("GET", "/api/search", (request, _) => Lib.Search(request.Query.GetValueOrDefault("q") ?? ""));
        Route("GET", $"/api/song/{SongId}", (_, a) =>
        {
            var detail = Lib.SongDetail(a[0]);
            Need(detail is not null);
            detail!["liked"] = _user.IsLiked(a[0]);
            return detail;
        });
        Route("POST", "/api/songs", (request, _) => new { songs = Lib.Views(request.Json().StrList("ids").Take(2000)) });
        Route("GET", $"/api/song/{SongId}/similar", (request, a) =>
        {
            Need(Lib.Songs.ContainsKey(a[0]));
            return new { songs = Lib.Similar(a[0], IntQuery(request, "n", 30)) };
        });
        Route("GET", "/api/resolve", (request, _) =>
        {
            var id = Lib.Resolve(request.Query.GetValueOrDefault("q") ?? "");
            Need(id is not null);
            return new { id };
        });
        Route("GET", $"/api/person/{PersonId}", (_, a) => Lib.PersonPage(a[0]) ?? throw new ApiException(404, "Not found"));
        Route("GET", $"/api/work/{SongId}/site", (_, a) => new { url = Lib.WorkSite(a[0]) ?? throw new ApiException(404, "Not found") });
        Route("GET", $"/api/work/{SongId}/art", async (_, a) =>
        {
            // The picture shown in the big view when a song has no clip; {"url": null} when there is none.
            var work = Lib.Works.GetValueOrDefault(a[0]) ?? throw new ApiException(404, "Not found");
            var picture = await _art.GetAsync(work);
            return new { url = picture?.Url, nsfw = picture?.Nsfw ?? false };
        });
        Route("GET", $"/api/work/{SongId}", (_, a) => Lib.WorkPage(a[0]) ?? throw new ApiException(404, "Not found"));
        Route("GET", "/api/tag/(.+)", (_, a) => Lib.TagPage(Uri.UnescapeDataString(a[0])));
        Route("GET", "/api/tags", (_, _) => new { tags = Lib.TagCounts() });

        Route("GET", "/api/likes", (_, _) =>
        {
            var ids = _user.LikedIds();
            return new { ids, songs = Lib.Views(ids) };
        });
        Route("POST", $"/api/likes/{SongId}", (_, a) =>
        {
            Need(Lib.Songs.ContainsKey(a[0]));
            _user.Like(a[0], true);
            return Ok;
        });
        Route("DELETE", $"/api/likes/{SongId}", (_, a) =>
        {
            _user.Like(a[0], false);
            return Ok;
        });
        Route("GET", "/api/recommended", (request, _) =>
        {
            int? seed = int.TryParse(request.Query.GetValueOrDefault("seed"), out var value) ? value : null;
            return new { songs = Lib.Recommend(_user.LikedIds(), IntQuery(request, "n", 60), seed) };
        });
        Route("POST", "/api/radio/next", (request, _) => RadioNext(request));

        Route("GET", "/api/playlists", (_, _) => new { playlists = _user.Playlists() });
        Route("POST", "/api/playlists", (request, _) =>
        {
            var body = request.Json();
            var name = (body.Str("name") ?? "").Trim();
            var ids = body.StrList("ids").Where(Lib.Songs.ContainsKey).ToList();
            return new { id = _user.CreatePlaylist(name.Length > 0 ? name : "New playlist", ids) };
        });
        Route("POST", "/api/anilist", async (request, _) =>
        {
            var body = request.Json();
            return await Import(() => _imports.AniListAsync(Lib, body.Str("user") ?? "", body.StrList("statuses")));
        });
        Route("POST", "/api/vndb", async (request, _) =>
        {
            var body = request.Json();
            return await Import(() => _imports.VndbAsync(Lib, body.Str("user") ?? "", body.StrList("labels"), body.Bool("bgm") ?? true));
        });
        Route("GET", "/api/playlists/([0-9]+)", (_, a) =>
        {
            var playlist = _user.Playlist(long.Parse(a[0]));
            Need(playlist is not null);
            playlist!["songs"] = Lib.Views(_user.PlaylistSongIds(long.Parse(a[0])));
            return playlist;
        });
        Route("PATCH", "/api/playlists/([0-9]+)", (request, a) =>
        {
            var name = (request.Json().Str("name") ?? "").Trim();
            Need(name.Length > 0, 400, "Name required");
            _user.RenamePlaylist(long.Parse(a[0]), name);
            return Ok;
        });
        Route("DELETE", "/api/playlists/([0-9]+)", (_, a) =>
        {
            _user.DeletePlaylist(long.Parse(a[0]));
            return Ok;
        });
        Route("POST", "/api/playlists/([0-9]+)/songs", (request, a) =>
        {
            Need(_user.Playlist(long.Parse(a[0])) is not null);
            var ids = request.Json().StrList("ids").Where(Lib.Songs.ContainsKey).ToList();
            return new { added = _user.AddToPlaylist(long.Parse(a[0]), ids) };
        });
        Route("DELETE", $"/api/playlists/([0-9]+)/songs/{SongId}", (_, a) =>
        {
            _user.RemoveFromPlaylist(long.Parse(a[0]), a[1]);
            return Ok;
        });
        Route("PUT", "/api/playlists/([0-9]+)/order", (request, a) =>
        {
            _user.ReorderPlaylist(long.Parse(a[0]), request.Json().StrList("ids"));
            return Ok;
        });
        Route("POST", "/api/played", (request, _) =>
        {
            var id = Json.Text(request.Json()["id"]);
            Need(id is not null, 400, "Song id required");
            _user.AddPlay(id!);
            return Ok;
        });
        Route("GET", "/api/recent", (_, _) => new { songs = Lib.Views(_user.RecentIds(100)) });
        Route("GET", "/api/export", (_, _) => _user.Export(Lib));
        Route("POST", "/api/import", (request, _) =>
        {
            try
            {
                return _user.Import(Lib, JsonNode.Parse(request.Body));
            }
            catch (Exception error) when (error is FormatException or JsonException or InvalidOperationException)
            {
                throw new ApiException(400, $"Could not import: {error.Message}");
            }
        });
    }

    private static readonly object Ok = new { ok = true };

    private static Dictionary<string, object?> With(Dictionary<string, object?> values, string key, object? value)
    {
        values[key] = value;
        return values;
    }

    // ---------------------------------------------------------------- app, setup, updates

    private Dictionary<string, object?> DiscordState()
    {
        var current = _settings.Current;
        return new()
        {
            ["available"] = true, ["on"] = current.Discord, ["appId"] = _discord.ClientId,
            ["custom"] = !string.IsNullOrEmpty(current.DiscordAppId), ["status"] = _discord.Status,
        };
    }

    private object AppInfoView()
    {
        var library = _state.Library;
        var current = _settings.Current;
        return new Dictionary<string, object?>
        {
            ["name"] = AppInfo.Name, ["version"] = AppInfo.VersionText, ["status"] = _state.Status, ["error"] = _state.Error,
            ["sources"] = library is null ? current.Sources : library.Sources.Order(StringComparer.Ordinal).ToList(),
            ["skipKinds"] = library is null ? current.SkipKinds : library.SkipKinds.Order(StringComparer.Ordinal).ToList(),
            ["kindCounts"] = library?.KindCounts(),
            ["library"] = library is null ? null : new Dictionary<string, object?>
            {
                ["version"] = library.Meta.GetValueOrDefault("version"), ["builtAt"] = library.Meta.GetValueOrDefault("builtAt"),
                ["dev"] = library.IsDev,
            },
            ["online"] = true, ["keySaved"] = !string.IsNullOrEmpty(current.Key),
            ["discord"] = DiscordState(),
            ["job"] = _updates.Job.State(),
        };
    }

    /// <summary>First start: check the access key, then download + decrypt the library (or decrypt a .mumu file picked
    /// by the user). Progress: GET /api/job.</summary>
    private async Task<object?> SetupAsync(HttpRequest request)
    {
        var body = request.Json();
        var key = (body.Str("key") ?? "").Trim();
        var file = body.Str("file");
        Need(key.Length > 0, 400, "Enter the access key");
        if (!string.IsNullOrEmpty(file))
        {
            _updates.Job.Start("library", () => _updates.InstallLibraryAsync(key, _state.Load, file, null));
            return Ok;
        }
        JsonObject manifest;
        try
        {
            manifest = await _updates.ManifestAsync();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new ApiException(502, $"Could not reach GitHub: {error.Message}");
        }
        Need(await Task.Run(() => LibraryCrypto.IsRightKey(key, manifest.Str("salt") ?? "", manifest.Str("check") ?? "")), 400, "Wrong access key");
        _updates.Job.Start("library", () => _updates.InstallLibraryAsync(key, _state.Load, null, manifest));
        return new { ok = true, version = manifest.Str("version") };
    }

    private async Task<object?> UpdatesAsync(bool refresh)
    {
        if (_updateCheck is null || refresh) _updateCheck = await _updates.CheckAsync(_state.Library?.IsDev ?? AppPaths.DevLibraryDb is not null);
        return new { app = UpdateService.AppView(_updateCheck.App), library = _updates.LibraryView(_updateCheck.Library) };
    }

    private object UpdateLibrary(string? newKey = null)
    {
        var manifest = _updateCheck?.Library ?? throw new ApiException(400, "No library update available");
        Need(_updates.LibraryView(manifest)!["needsApp"]!.GetValue<bool>() == false, 400, "This library needs a newer version of the app");
        var key = newKey ?? KeyProtection.Unprotect(_settings.Current.Key);
        Need(!string.IsNullOrEmpty(key), 400, "No access key saved");
        if (!LibraryCrypto.IsRightKey(key!, manifest.Str("salt") ?? "", manifest.Str("check") ?? ""))
        {
            _settings.Update(s => s.Key = null);
            throw new ApiException(400, "The access key changed: enter the new one");
        }
        _updates.Job.Start("library", () => _updates.InstallLibraryAsync(key!, () =>
        {
            _updateCheck!.Library = null;
            _state.Load();
        }, null, manifest));
        return Ok;
    }

    /// <summary>A new access key for the library update (when the old one stopped working).</summary>
    private object UpdateKey(HttpRequest request)
    {
        var manifest = _updateCheck?.Library;
        var key = (request.Json().Str("key") ?? "").Trim();
        if (manifest is null || key.Length == 0) throw new ApiException(400, "Enter the access key");
        Need(LibraryCrypto.IsRightKey(key, manifest.Str("salt") ?? "", manifest.Str("check") ?? ""), 400, "Wrong access key");
        _settings.Update(s => s.Key = KeyProtection.Protect(key));
        return UpdateLibrary(key);
    }

    private object UpdateApp()
    {
        var release = _updateCheck?.App;
        Need(release is not null && AppPaths.IsInstalled && release.Download is not null, 400, "No app update to install");
        _updates.Job.Start("app", () => _updates.InstallAppAsync(release!, _window.QuitAndInstall));
        return Ok;
    }

    // ---------------------------------------------------------------- settings

    private object SetSources(HttpRequest request)
    {
        var sources = request.Json().StrList("sources").Where(AppInfo.Sources.Contains).Distinct().ToList();
        Need(sources.Count > 0, 400, "Pick at least one library");
        _settings.Update(s => s.Sources = sources);
        _state.Library?.SetSources(sources, _state.Library.SkipKinds);
        return Ok;
    }

    private object SetSongKinds(HttpRequest request)
    {
        var known = AppInfo.SongKinds.SelectMany(p => p.Value.Select(kind => $"{p.Key}:{kind}")).ToHashSet();
        var skip = request.Json().StrList("skip").Where(known.Contains).Distinct().Order(StringComparer.Ordinal).ToList();
        foreach (var (source, kinds) in AppInfo.SongKinds)
        {
            Need(kinds.Any(kind => !skip.Contains($"{source}:{kind}")), 400, "Keep at least one kind of song on");
        }
        _settings.Update(s => s.SkipKinds = skip);
        _state.Library?.SetSources(_state.Library.Sources, skip);
        return new { skip };
    }

    /// <summary>{"on": bool} switches the status on / off; {"appId": "..."} sets the application id after testing it
    /// against the running Discord ("" goes back to the one built into the app).</summary>
    private object SetDiscord(HttpRequest request)
    {
        var body = request.Json();
        if (body.ContainsKey("appId"))
        {
            var appId = (body.Str("appId") ?? "").Trim();
            if (appId.Length > 0)
            {
                try
                {
                    DiscordPresence.Test(appId);
                }
                catch (DiscordException error)
                {
                    throw new ApiException(400, error.Message);
                }
            }
            _settings.Update(s =>
            {
                s.DiscordAppId = appId.Length > 0 ? appId : null;
                s.Discord = true;
            });
            _discord.Reset();
        }
        if (body.Bool("on") is { } on)
        {
            _settings.Update(s => s.Discord = on);
            if (!on) _discord.Update(null);
        }
        return DiscordState();
    }

    // ---------------------------------------------------------------- pages

    private object Home()
    {
        var library = Lib;
        var liked = _user.LikedIds();
        var active = library.Sources.Order(StringComparer.Ordinal).ToList();
        return new
        {
            stats = new Dictionary<string, object?>
            {
                ["songs"] = library.ActiveSongCount(), ["analysed"] = library.ActiveAnalysedCount, ["liked"] = liked.Count,
                ["playlists"] = _user.Playlists().Count,
                ["bySource"] = active.ToDictionary(source => source, source => library.ActiveSongCount(source)),
            },
            recent = library.Views(_user.RecentIds(16)),
            recommended = liked.Count > 0 ? library.Recommend(liked, 12) : [],
            discover = library.RandomPicks(),
            tags = library.TagCounts(),
        };
    }

    private object RadioNext(HttpRequest request)
    {
        var body = request.Json();
        var seed = Json.Text(body["seed"]);
        Need(seed is not null, 400, "No seed song");
        var current = Json.Text(body["current"]) is { Length: > 0 } now ? now : seed!;
        var song = Lib.RadioNext(seed!, current, body.StrList("history").TakeLast(600).ToList(), body.Str("mode") ?? "close",
            body.StrList("skipped"), body.StrList("liked"));
        Need(song is not null, 404, "No more similar songs found");
        return song!;
    }

    private static async Task<object?> Import(Func<Task<Dictionary<string, object?>>> import)
    {
        try
        {
            return await import();
        }
        catch (ImportException error)
        {
            throw new ApiException(error.Status, error.Message);
        }
    }

    // ---------------------------------------------------------------- http

    public async Task HandleAsync(HttpRequest request, HttpResponse response)
    {
        // DNS rebinding and other pages: only this origin's page talks to the server.
        var host = request.Header("Host");
        var origin = request.Header("Origin");
        if ((host is not null && !_origin.EndsWith("//" + host, StringComparison.OrdinalIgnoreCase))
            || (origin is not null && origin != "null" && !string.Equals(origin, _origin, StringComparison.OrdinalIgnoreCase)))
        {
            response.KeepAlive = false;
            await response.JsonAsync(403, new { error = "Forbidden" });
            return;
        }
        if (request.Path.StartsWith("/media/", StringComparison.Ordinal) && request.Method == "GET")
        {
            await MediaAsync(request, response);
            return;
        }
        if (request.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            await ApiAsync(request, response);
            return;
        }
        if (request.Method != "GET")
        {
            await response.JsonAsync(405, new { error = "Method not allowed" });
            return;
        }
        await StaticAsync(request, response);
    }

    private async Task ApiAsync(HttpRequest request, HttpResponse response)
    {
        foreach (var (method, pattern, handler) in _routes)
        {
            var match = pattern.Match(request.Path);
            if (method != request.Method || !match.Success) continue;
            int status;
            object? body;
            try
            {
                (status, body) = (200, await handler(request, match.Groups.Values.Skip(1).Select(g => g.Value).ToArray()));
            }
            catch (ApiException error)
            {
                (status, body) = (error.Status, new { error = error.Message });
            }
            catch (UpdateException error)
            {
                (status, body) = (409, new { error = error.Message });
            }
            catch (Exception error) when (error is JsonException or FormatException)
            {
                (status, body) = (400, new { error = $"Bad request: {error.Message}" });
            }
            catch (Exception error)
            {
                Log.Error($"{request.Method} {request.Path}", error);
                (status, body) = (500, new { error = $"{error.GetType().Name}: {error.Message}" });
            }
            await response.JsonAsync(status, body);
            return;
        }
        await response.JsonAsync(404, new { error = "Unknown endpoint" });
    }

    /// <summary>/media/&lt;file&gt; -> the EMQ file, Range passed through so seeking works.</summary>
    private async Task MediaAsync(HttpRequest request, HttpResponse response)
    {
        var path = Uri.UnescapeDataString(request.Path["/media/".Length..]);
        var library = _state.Library;
        // only the library's own files
        if (library is null || !library.FileToId.TryGetValue(path, out var id) || library.Songs[id].Src != "emq")
        {
            await response.JsonAsync(404, new { error = "Unknown file" });
            return;
        }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        HttpResponseMessage upstream;
        try
        {
            upstream = await _media.GetAsync(path, request.Header("Range"), cancel.Token);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            await response.JsonAsync(502, new { error = $"EMQ: {error.Message}" });
            return;
        }
        using (upstream)
        {
            var status = (int)upstream.StatusCode;
            if (status >= 400)
            {
                await response.JsonAsync(status, new { error = $"EMQ answered {status}" });
                return;
            }
            var headers = new List<(string, string)>();
            foreach (var name in MediaHeaders)
            {
                if (upstream.Headers.TryGetValues(name, out var values) || upstream.Content.Headers.TryGetValues(name, out values))
                {
                    headers.Add((name, string.Join(", ", values)));
                }
            }
            if (headers.All(h => h.Item1 != "Accept-Ranges")) headers.Add(("Accept-Ranges", "bytes"));
            headers.Add(("Cache-Control", "private, max-age=86400"));
            if (upstream.Content.Headers.ContentLength is null) response.KeepAlive = false;
            await response.HeadAsync(status, headers);
            await using var stream = await upstream.Content.ReadAsStreamAsync(cancel.Token);
            await stream.CopyToAsync(response.Stream, 65536, cancel.Token);
        }
    }

    private static async Task StaticAsync(HttpRequest request, HttpResponse response)
    {
        var root = Path.GetFullPath(AppPaths.WebRoot);
        var relative = Uri.UnescapeDataString(request.Path).TrimStart('/');
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (relative.Length == 0 || !path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            path = Path.Combine(root, "index.html");
        }
        var type = StaticTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        await response.SendAsync(200, await File.ReadAllBytesAsync(path), type);
    }
}
