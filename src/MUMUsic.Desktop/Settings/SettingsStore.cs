namespace MUMUsic.Desktop.Settings;

/// <summary>settings.json in the user folder. The same file and keys as the 1.x app, so nothing is lost on update.</summary>
internal sealed class AppSettings
{
    public List<string> Sources { get; set; } = [.. AppInfo.Sources];
    /// <summary>Song kinds never picked for the user ("amq:INS", "emq:BGM"), see <see cref="AppInfo.SongKinds"/>.</summary>
    public List<string> SkipKinds { get; set; } = [];
    /// <summary>Access key, protected with DPAPI (<see cref="Security.KeyProtection"/>).</summary>
    public string? Key { get; set; }
    /// <summary>[width, height] of the restored window (frame included), in pixels at 96 DPI.</summary>
    public int[]? Window { get; set; }
    /// <summary>[left, top] of the restored window, in screen pixels.</summary>
    public int[]? WindowAt { get; set; }
    public bool Maximized { get; set; }
    /// <summary>Local server port, kept so the window's storage (theme, volume, queue) belongs to the same origin.</summary>
    public int? Port { get; set; }
    public string? LibraryVersion { get; set; }
    public string? SkipAppVersion { get; set; }
    public bool Discord { get; set; } = true;
    /// <summary>Discord application id pasted on the Data page (else <see cref="AppInfo.DiscordClientId"/>).</summary>
    public string? DiscordAppId { get; set; }

    public AppSettings Copy() => (AppSettings)MemberwiseClone();
}

internal sealed class SettingsStore(string path)
{
    private readonly object _gate = new();
    private AppSettings _current = Normalize(Json.ReadFile(path, new AppSettings()));

    /// <summary>A snapshot: change settings through <see cref="Update"/>.</summary>
    public AppSettings Current
    {
        get
        {
            lock (_gate) return _current.Copy();
        }
    }

    public AppSettings Update(Action<AppSettings> change)
    {
        lock (_gate)
        {
            var next = _current.Copy();
            change(next);
            Json.WriteFile(path, next);
            _current = next;
            return next.Copy();
        }
    }

    // A file edited by hand (or written by an older app) may hold nulls where a list is expected.
    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Sources = settings.Sources?.Where(AppInfo.Sources.Contains).Distinct().ToList() ?? [];
        if (settings.Sources.Count == 0) settings.Sources = [.. AppInfo.Sources];
        settings.SkipKinds ??= [];
        return settings;
    }
}
