using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MUMUsic.Desktop.Security;
using MUMUsic.Desktop.Settings;
using MUMUsic.Desktop.UserData;

namespace MUMUsic.Desktop.Updates;

/// <summary>A failure the UI shows as is ("No library update available", "The download is damaged").</summary>
internal sealed class UpdateException(string message) : Exception(message);

/// <summary>A newer app release: its MUMUsic-Setup-&lt;version&gt;.exe on GitHub.</summary>
internal sealed record AppRelease(string Version, string Notes, string Page, string? Download, long? Size, string? Sha256);

/// <summary>
/// Updates from GitHub, like Ruruka: the app (release "v&lt;version&gt;", one asset MUMUsic-Setup-&lt;version&gt;.exe) and the
/// encrypted library (release "library": manifest.json + library.mumu).
///
/// An app update downloads the setup, quits, and runs it with /SILENT (Program.cs). The setup (installer/MUMUsic.iss)
/// waits for the instance mutex to go, replaces the program files and starts the app again. The user folder is never
/// touched, and user.db is backed up first.
/// </summary>
internal sealed partial class UpdateService(SettingsStore settings, UserStore user)
{
    private static readonly HttpClient Http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", AppInfo.UserAgent } },
    };

    [GeneratedRegex(@"^v\d+(\.\d+)*$")]
    private static partial Regex VersionTag();

    public UpdateJob Job { get; } = new();

    /// <summary>Deletes the setups of past updates from downloads\. One still finishing (it starts the app before it
    /// exits) is locked: it goes on the next start.</summary>
    public static void DeleteOldSetups()
    {
        if (!Directory.Exists(AppPaths.DownloadDir)) return;
        foreach (var setup in Directory.GetFiles(AppPaths.DownloadDir, AppInfo.SetupPrefix + "*.exe"))
        {
            try
            {
                File.Delete(setup);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>"1.10.2" -> (1, 10, 2); anything else -> (0).</summary>
    internal static int[] VersionParts(string? version)
    {
        var parts = Regex.Matches(version ?? "", @"\d+").Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToArray();
        return parts.Length > 0 ? parts : [0];
    }

    internal static int CompareVersions(string? a, string? b)
    {
        var x = VersionParts(a);
        var y = VersionParts(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var left = i < x.Length ? x[i] : -1;
            var right = i < y.Length ? y[i] : -1;
            if (left != right) return left.CompareTo(right);
        }
        return 0;
    }

    private static string ReleaseUrl(string tag, string asset) => $"https://github.com/{AppInfo.GitHubRepo}/releases/download/{tag}/{asset}";

    public async Task<JsonObject> ManifestAsync()
    {
        var url = ReleaseUrl(AppInfo.LibraryRelease, AppInfo.ManifestAsset);
        return await Http.GetFromJsonAsync<JsonObject>(url, Json.Options) ?? throw new UpdateException("The library manifest is empty");
    }

    /// <summary>Newest "v&lt;version&gt;" release. The library release is not an app release, so GitHub's "latest" is
    /// not trusted: the tags are compared.</summary>
    public async Task<AppRelease?> LatestAppAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{AppInfo.GitHubRepo}/releases?per_page=30");
        request.Headers.Add("Accept", "application/vnd.github+json");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var releases = await response.Content.ReadFromJsonAsync<JsonArray>() ?? [];
        var best = releases.OfType<JsonObject>()
            .Where(r => r.Bool("draft") != true && r.Bool("prerelease") != true && VersionTag().IsMatch(r.Str("tag_name") ?? ""))
            .OrderByDescending(r => r.Str("tag_name"), Comparer<string?>.Create(CompareVersions))
            .FirstOrDefault();
        if (best is null) return null;
        // Only a setup hosted on this repository's releases is ever run, never an arbitrary link.
        var asset = (best["assets"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(a =>
            a.Str("name") is { } name && name.StartsWith(AppInfo.SetupPrefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && a.Str("browser_download_url") is { } link
            && link.StartsWith($"https://github.com/{AppInfo.GitHubRepo}/releases/download/", StringComparison.OrdinalIgnoreCase));
        var notes = (best.Str("body") ?? "").Trim();
        var digest = asset?.Str("digest");
        return new AppRelease(best.Str("tag_name")![1..], notes.Length > 2000 ? notes[..2000] : notes, best.Str("html_url") ?? "",
            asset?.Str("browser_download_url"), asset?["size"]?.GetValue<long>(),
            digest is not null && digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest[7..] : null);
    }

    /// <summary>What can be updated: {"app": {...} | null, "library": {...} | null}. A network error leaves a part null.</summary>
    public async Task<UpdateCheck> CheckAsync(bool hasDevLibrary)
    {
        var check = new UpdateCheck();
        try
        {
            if (await LatestAppAsync() is { } app && CompareVersions(app.Version, AppInfo.VersionText) > 0
                && app.Version != settings.Current.SkipAppVersion)
            {
                check.App = app;
            }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error("update app check", error);
        }
        if (hasDevLibrary) return check;
        try
        {
            var manifest = await ManifestAsync();
            if (manifest.Str("version") != settings.Current.LibraryVersion) check.Library = manifest;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error("update library check", error);
        }
        return check;
    }

    /// <summary>The app update as the UI reads it.</summary>
    public static Dictionary<string, object?>? AppView(AppRelease? app) => app is null ? null : new()
    {
        ["version"] = app.Version, ["notes"] = app.Notes, ["page"] = app.Page, ["download"] = app.Download, ["size"] = app.Size,
        ["current"] = AppInfo.VersionText, ["canInstall"] = AppPaths.IsInstalled && app.Download is not null,
    };

    /// <summary>The library update as the UI reads it: the manifest plus what the app makes of it.</summary>
    public JsonObject? LibraryView(JsonObject? manifest)
    {
        if (manifest is null) return null;
        var view = manifest.DeepClone().AsObject();
        view["current"] = settings.Current.LibraryVersion;
        var format = manifest["format"] is JsonValue f && f.TryGetValue<int>(out var number) ? number : 1;
        view["needsApp"] = format > AppInfo.LibraryFormat || CompareVersions(manifest.Str("minApp"), AppInfo.VersionText) > 0;
        return view;
    }

    private async Task DownloadAsync(string url, string destination, long? size, string? sha256)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = size ?? response.Content.Headers.ContentLength ?? 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(destination))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                hash.AppendData(buffer, 0, read);
                done += read;
                if (total > 0) Job.Set(progress: Math.Min(1.0, (double)done / total));
            }
        }
        if (sha256 is not null && !Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new UpdateException("The download is damaged (checksum mismatch), try again");
        }
    }

    /// <summary>Downloads (or takes <paramref name="sourceFile"/>), decrypts into the user folder, then calls
    /// <paramref name="onDone"/> to load it. <paramref name="manifest"/> is set when it comes from GitHub.</summary>
    public async Task InstallLibraryAsync(string passphrase, Action onDone, string? sourceFile, JsonObject? manifest)
    {
        var encrypted = sourceFile ?? Path.Combine(AppPaths.DownloadDir, AppInfo.LibraryAsset);
        if (sourceFile is null)
        {
            await DownloadAsync(ReleaseUrl(AppInfo.LibraryRelease, AppInfo.LibraryAsset), encrypted,
                manifest?["size"]?.GetValue<long>(), manifest?.Str("sha256"));
        }
        user.Backup("before-library-update");
        Job.Set(step: "decrypting", progress: 0);
        var fresh = Path.ChangeExtension(AppPaths.LibraryDb, ".new");
        await Task.Run(() => LibraryCrypto.DecryptFile(encrypted, fresh, passphrase, p => Job.Set(progress: p)));
        File.Move(fresh, AppPaths.LibraryDb, overwrite: true);
        if (sourceFile is null) File.Delete(encrypted);
        var version = manifest?.Str("version") ?? "local";
        settings.Update(s =>
        {
            s.Key = KeyProtection.Protect(passphrase);
            s.LibraryVersion = version;
        });
        Job.Set(step: "installing");
        onDone();
    }

    /// <summary>Downloads the new setup, then hands its path to <paramref name="quitAndInstall"/>: the app quits and
    /// Program.cs starts the setup once the instance mutex is released.</summary>
    public async Task InstallAppAsync(AppRelease release, Action<string> quitAndInstall)
    {
        user.Backup("before-app-update");
        var setup = Path.Combine(AppPaths.DownloadDir, $"{AppInfo.SetupPrefix}{release.Version}.exe");
        await DownloadAsync(release.Download!, setup, release.Size, release.Sha256);
        Job.Set(step: "installing");
        quitAndInstall(setup);
    }
}

internal sealed class UpdateCheck
{
    public AppRelease? App { get; set; }
    public JsonObject? Library { get; set; }
}

/// <summary>The download / install running in the background; the UI polls <see cref="State"/> (GET /api/job).</summary>
internal sealed class UpdateJob
{
    private readonly object _gate = new();
    private string? _kind;
    private string _step = "idle";
    private double _progress;
    private string? _error;

    public Dictionary<string, object?> State()
    {
        lock (_gate) return new() { ["kind"] = _kind, ["step"] = _step, ["progress"] = _progress, ["error"] = _error };
    }

    public void Set(string? step = null, double? progress = null)
    {
        lock (_gate)
        {
            if (step is not null) _step = step;
            if (progress is { } value) _progress = value;
        }
    }

    public void Start(string kind, Func<Task> work)
    {
        lock (_gate)
        {
            if (_step is "downloading" or "decrypting" or "installing") throw new UpdateException("An update is already running");
            (_kind, _step, _progress, _error) = (kind, "downloading", 0, null);
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
                Set(step: "done", progress: 1);
            }
            catch (Exception error)
            {
                Log.Error($"update {kind}", error);
                var message = error is HttpRequestException ? $"Could not download: {error.Message}" : error.Message;
                lock (_gate) (_step, _error) = ("error", message);
            }
        });
    }
}
