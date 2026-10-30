using System.Text;

namespace MUMUsic.Desktop;

/// <summary>The app has no console: messages and errors go to mumusic.log in the user folder, started over once it
/// passes 1 MB.</summary>
internal static class Log
{
    private const long MaxSize = 1_000_000;
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static void Open()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserDir);
            var info = new FileInfo(AppPaths.LogFile);
            var append = !info.Exists || info.Length <= MaxSize;
            var stream = new FileStream(AppPaths.LogFile, append ? FileMode.Append : FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        catch (IOException)
        {
            _writer = null;
        }
        Write($"--- {AppInfo.Name} {AppInfo.VersionText} started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            System.Diagnostics.Debug.WriteLine(message);
            try
            {
                _writer?.WriteLine(message);
            }
            catch (IOException)
            {
            }
        }
    }

    public static void Error(string where, Exception error) => Write($"[{where}] {error.GetType().Name}: {error.Message}");
}
