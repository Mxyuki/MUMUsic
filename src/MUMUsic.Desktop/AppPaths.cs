namespace MUMUsic.Desktop;

/// <summary>Where things are on disk. The program folder is replaced by every update; the user folder never is.</summary>
internal static class AppPaths
{
    /// <summary>%APPDATA%\MUMUsic: user.db, settings.json, the decrypted library, the WebView storage.</summary>
    public static string UserDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.Name);

    public static string SettingsFile => Path.Combine(UserDir, "settings.json");
    public static string UserDb => Path.Combine(UserDir, "user.db");
    public static string LibraryDb => Path.Combine(UserDir, "library.db");
    public static string DownloadDir => Path.Combine(UserDir, "downloads");
    public static string BackupDir => Path.Combine(UserDir, "backups");
    public static string EmqSessionFile => Path.Combine(UserDir, "emq-session.json");
    public static string ArtCacheFile => Path.Combine(UserDir, "art-cache.json");
    public static string LogFile => Path.Combine(UserDir, "mumusic.log");

    // The 1.x app kept its WebView storage here too: the same folder (and the same page origin) keeps the theme,
    // volume and queue across the move to this host.
    public static string WebViewData => Path.Combine(UserDir, "webview");

    public static string ProgramDir { get; } = AppContext.BaseDirectory;

    public static string WebRoot => Path.Combine(ProgramDir, "web");

    /// <summary>Installed by the setup (Inno Setup leaves its uninstaller next to the app). Only an installed app
    /// updates itself; a build run from the source tree just links to the release page.</summary>
    public static bool IsInstalled => File.Exists(Path.Combine(ProgramDir, "unins000.exe"));

    /// <summary>The maintainer's plain library (data/library.db in the source tree), used as is when the app runs from
    /// a build of that tree: no key, no download.</summary>
    public static string? DevLibraryDb { get; } = FindDevLibrary();

    private static string? FindDevLibrary()
    {
        if (IsInstalled) return null;
        for (var dir = new DirectoryInfo(ProgramDir); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "MUMUsic.sln"))) continue;
            var path = Path.Combine(dir.FullName, "data", "library.db");
            return File.Exists(path) ? path : null;
        }
        return null;
    }
}
