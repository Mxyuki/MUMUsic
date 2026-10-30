using System.Reflection;

namespace MUMUsic.Desktop;

/// <summary>Names, URLs and constants. Nothing else in the app hard-codes a host or a release name.</summary>
internal static class AppInfo
{
    public const string Name = "MUMUsic";

    /// <summary>"owner/repository" on GitHub: app releases ("v&lt;version&gt;") and the library release.</summary>
    public const string GitHubRepo = "Mxyuki/MUMUsic";
    public const string LibraryRelease = "library";
    public const string LibraryAsset = "library.mumu";
    public const string ManifestAsset = "manifest.json";
    /// <summary>The one asset of an app release: MUMUsic-Setup-&lt;version&gt;.exe, built by scripts/release.ps1.</summary>
    public const string SetupPrefix = "MUMUsic-Setup-";

    /// <summary>Library files newer than this format need a newer app (manifest "format", tools/build_library.py).</summary>
    public const int LibraryFormat = 1;

    /// <summary>Application id from discord.com/developers (public, not a secret).</summary>
    public const string DiscordClientId = "1563715346635038792";
    // Discord caches an app's asset list in each client, so an asset name only works once that cache has caught up;
    // a URL works right away.
    public const string DiscordImage = "https://cdn.discordapp.com/app-assets/1563715346635038792/1563717609092808775.png";

    public const string AmqMedia = "https://eudist.animemusicquiz.com/";
    public const string EmqSite = "https://erogemusicquiz.com";
    public const string EmqMedia = EmqSite + "/selfhoststorage/";
    public const string AniListSite = "https://anilist.co";
    public const string AniListApi = "https://graphql.anilist.co";
    public const string VndbSite = "https://vndb.org";
    public const string VndbApi = "https://api.vndb.org/kana";

    /// <summary>Page behind the "Listen on MUMUsic" button of the Discord status (docs/play.html, GitHub Pages). Discord
    /// buttons only open https links, so the page opens the mumusic:// link that starts the app.</summary>
    public const string ListenPage = "https://mxyuki.github.io/MUMUsic/play.html";
    /// <summary>mumusic://play/&lt;song id&gt;: registered by the setup (installer/MUMUsic.iss), handled in Program.cs.</summary>
    public const string LinkScheme = "mumusic";

    /// <summary>Held while the app runs: a second start shows the first window, and the installer waits for it.</summary>
    public const string InstanceMutex = "MUMUsic.SingleInstance";
    public const string ShowWindowEvent = "MUMUsic.ShowWindow";
    /// <summary>Set by the setup and the uninstaller (installer/MUMUsic.iss) to close the app before they replace it.</summary>
    public const string QuitEvent = "MUMUsic.Quit";

    public static readonly string[] Sources = ["amq", "emq"];

    /// <summary>Kinds of song the user can turn off ("amq:INS"): the first word of a song's type, OTHER for EMQ songs
    /// without one.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> SongKinds = new Dictionary<string, string[]>
    {
        ["amq"] = ["OP", "ED", "INS"],
        ["emq"] = ["OP", "ED", "INS", "BGM", "OTHER"],
    };

    public static Version Version { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version);

    /// <summary>"2.0.0": the version shown in the UI and compared with the release tags.</summary>
    public static string VersionText { get; } = Version.ToString(3);

    public static string UserAgent { get; } = $"{Name}/{VersionText}";

    private static Version Normalize(Version? version) =>
        version is null ? new Version(0, 0, 0) : new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
}
