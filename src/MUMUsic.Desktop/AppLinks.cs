using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;

namespace MUMUsic.Desktop;

/// <summary>
/// mumusic://play/&lt;song id&gt; links (the "Listen on MUMUsic" button of a friend's Discord status, through
/// docs/play.html). Windows starts MUMUsic.exe with the link; when the app already runs, that second start hands the link
/// to it over a named pipe and exits.
/// </summary>
internal static partial class AppLinks
{
    private const string PipeName = "MUMUsic.Open";

    [GeneratedRegex(@"^mumusic://play/([ae][0-9]+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex PlayLink();

    /// <summary>The song a link asks to play, or null.</summary>
    public static string? SongId(string? link)
    {
        var match = PlayLink().Match(link?.Trim() ?? "");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static string? FromArgs(string[] args) =>
        args.FirstOrDefault(a => a.StartsWith(AppInfo.LinkScheme + ":", StringComparison.OrdinalIgnoreCase));

    /// <summary>Second start: gives the link to the running app.</summary>
    public static void Send(string link)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(3000);
            pipe.Write(Encoding.UTF8.GetBytes(link + "\n"));
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Error("send link", error);
        }
    }

    /// <summary>First start: hands every link sent by a second start to <paramref name="open"/> (on a background thread).</summary>
    public static void Listen(Action<string> open)
    {
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    if (reader.ReadLine() is { } link) open(link);
                }
                catch (IOException error)
                {
                    Log.Error("receive link", error);
                    Thread.Sleep(1000);
                }
            }
        }) { IsBackground = true, Name = "Links" }.Start();
    }
}
