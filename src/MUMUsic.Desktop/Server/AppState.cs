using MUMUsic.Desktop.Library;
using MUMUsic.Desktop.Settings;

namespace MUMUsic.Desktop.Server;

/// <summary>The library and where its loading stands. Until it is ready, /api/app says what is going on (loading, or
/// setup needed: first start without a library) and the other API calls answer 503.</summary>
internal sealed class AppState(SettingsStore settings)
{
    /// <summary>loading | setup | ready | error</summary>
    public string Status { get; private set; } = "loading";
    public string? Error { get; private set; }
    public MusicLibrary? Library { get; private set; }

    private static string? LibraryPath() =>
        AppPaths.DevLibraryDb ?? (File.Exists(AppPaths.LibraryDb) ? AppPaths.LibraryDb : null);

    /// <summary>(Re)loads the library in the background. The old one stays in use until the new one is ready.</summary>
    public void LoadInBackground() => Task.Run(Load);

    /// <summary>For update jobs: their "done" means the new library is in use.</summary>
    public void Load()
    {
        var path = LibraryPath();
        if (path is null)
        {
            Status = "setup";
            return;
        }
        try
        {
            var current = settings.Current;
            var started = DateTime.UtcNow;
            var library = new MusicLibrary(path, path == AppPaths.DevLibraryDb, current.Sources, current.SkipKinds);
            (Library, Status, Error) = (library, "ready", null);
            // Loading leaves a lot of garbage behind (and a reload, the whole previous library): give it back now.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            Log.Write($"[library] {library.Songs.Count} songs, {library.AnalysedCount} analysed, loaded in {(DateTime.UtcNow - started).TotalSeconds:0.0} s, {GC.GetTotalMemory(false) >> 20} MB");
        }
        catch (Exception error)
        {
            Log.Error($"library {path}", error);
            // A damaged download is set up again; the maintainer's own library shows its error.
            (Status, Error) = path == AppPaths.LibraryDb ? ("setup", null) : ("error", error.Message);
        }
    }
}
