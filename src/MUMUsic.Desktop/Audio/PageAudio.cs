using System.Net.WebSockets;

namespace MUMUsic.Desktop.Audio;

/// <summary>
/// Plays the page's music from this process. WebView2 renders sound in its own msedgewebview2.exe processes, and
/// Discord's "share application audio" only captures the shared window's process: friends watching a stream of the app
/// heard nothing. So the page sends its final mix (after volume levelling) over a WebSocket as 16-bit stereo PCM
/// (web/app.js, "host audio output") and stays silent itself while the socket is open.
/// </summary>
internal static class PageAudio
{
    private const int MaxMessageBytes = 256 * 1024;

    /// <summary>Plays what one page connection sends until it closes.</summary>
    public static async Task ReceiveAsync(WebSocket socket, int sampleRate)
    {
        using var output = new WaveOutput(sampleRate);
        var buffer = new byte[MaxMessageBytes];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var filled = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (filled == buffer.Length) throw new WebSocketException("Audio message too large");
                    result = await socket.ReceiveAsync(buffer.AsMemory(filled), CancellationToken.None);
                    filled += result.Count;
                }
                while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    return;
                }
                if (result.MessageType == WebSocketMessageType.Binary) output.Write(buffer.AsSpan(0, filled));
            }
        }
        catch (Exception error) when (error is WebSocketException or IOException or ObjectDisposedException)
        {
            // The page was reloaded or the app is quitting.
        }
    }
}
