using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MUMUsic.Desktop.Settings;

namespace MUMUsic.Desktop.Integrations;

internal sealed class DiscordException(string message) : Exception(message);

/// <summary>
/// Discord status ("Listening to MUMUsic"): the playing song, its artist, anime / VN and type, with a progress bar.
/// Talks to the Discord app on this computer over its local pipe (\\.\pipe\discord-ipc-N, the protocol of Discord's RPC
/// SDK: 8-byte header = opcode + length, then JSON). Discord only shows a status attached to an application made at
/// discord.com/developers. Updates are coalesced: Discord accepts about 5 per 20 seconds, so a burst of seeks sends only
/// the last state. When Discord isn't running, it tries again every 20 seconds.
/// </summary>
internal sealed partial class DiscordPresence
{
    private const int OpHandshake = 0, OpFrame = 1;
    private const int Listening = 2;                                       // activity type: "Listening to ..."
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(20);

    [GeneratedRegex(@"^\d{17,20}$")]
    private static partial Regex AppIdPattern();

    [GeneratedRegex(@"^[ae][0-9]+$")]
    private static partial Regex SongIdPattern();

    private readonly SettingsStore _settings;
    private readonly AutoResetEvent _wake = new(false);
    private NamedPipeClientStream? _pipe;
    private string? _pipeAppId;
    private string? _wanted, _sent;                // latest activity asked for / sent, as JSON (null = no status)
    private DateTime _lastSend = DateTime.MinValue;

    public DiscordPresence(SettingsStore settings)
    {
        _settings = settings;
        new Thread(Loop) { IsBackground = true, Name = "Discord status" }.Start();
    }

    /// <summary>Shown on the Data page.</summary>
    public string Status { get; private set; } = "idle";

    public string ClientId => _settings.Current.DiscordAppId is { Length: > 0 } custom ? custom : AppInfo.DiscordClientId;

    /// <summary>song: {name, artist, work, workKind, type, position, duration, playing}, or null to clear.</summary>
    public void Update(JsonObject? song)
    {
        _wanted = song is null ? null : Activity(song).ToJsonString();
        _wake.Set();
    }

    /// <summary>The application id changed: reconnect with it and send the current song again.</summary>
    public void Reset()
    {
        _sent = "reset";
        _wake.Set();
    }

    /// <summary>Checks an application id against the running Discord.</summary>
    public static void Test(string appId)
    {
        if (!AppIdPattern().IsMatch(appId)) throw new DiscordException("An application id is a number of 17 to 20 digits");
        OpenPipe(appId).Dispose();
    }

    private static JsonObject Activity(JsonObject song)
    {
        var kind = song.Str("workKind") switch { "anime" => "Anime", "vn" => "VN", _ => "" };
        var about = string.Join(" · ", new[] { song.Str("type"), kind }.Where(x => !string.IsNullOrEmpty(x)));
        var work = song.Str("work") ?? "";
        var activity = new JsonObject
        {
            ["type"] = Listening,
            ["details"] = Cut(song.Str("name") is { Length: > 0 } name ? name : "?"),
            ["state"] = Cut(song.Str("artist") is { Length: > 0 } artist ? artist : "Unknown artist"),
            ["assets"] = new JsonObject
            {
                ["large_image"] = AppInfo.DiscordImage,
                ["large_text"] = Cut(work + (about.Length > 0 ? $" ({about})" : "")),
            },
        };
        // Discord shows the button to the people looking at the status (never to the one listening).
        if (song.Str("id") is { } id && SongIdPattern().IsMatch(id))
        {
            activity["buttons"] = new JsonArray(new JsonObject { ["label"] = "Listen on MUMUsic", ["url"] = ListenUrl(id, song) });
        }
        var duration = Number(song["duration"]);
        if (song.Bool("playing") == true && duration > 0)
        {
            // a start and an end give the progress bar with the current time / the length
            var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)(Number(song["position"]) * 1000);
            activity["timestamps"] = new JsonObject { ["start"] = start, ["end"] = start + (long)(duration * 1000) };
        }
        else
        {
            activity["state"] = Cut($"Paused · {activity["state"]!.GetValue<string>()}");
        }
        return activity;
    }

    /// <summary>The page link, with the title and artist for the page to show (Discord takes 512 characters at most).</summary>
    private static string ListenUrl(string id, JsonObject song)
    {
        string Part(string? text) => Uri.EscapeDataString(text is { Length: > 80 } ? text[..80] : text ?? "");
        return $"{AppInfo.ListenPage}?song={id}&name={Part(song.Str("name"))}&artist={Part(song.Str("artist"))}";
    }

    private static double Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0;

    private static string Cut(string text) => text.Length <= 128 ? text : text[..128];

    private static NamedPipeClientStream OpenPipe(string appId)
    {
        for (var n = 0; n < 10; n++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{n}", PipeDirection.InOut);
            JsonObject answer;
            int op;
            try
            {
                pipe.Connect(200);
                Write(pipe, OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = appId });
                (op, answer) = Read(pipe);
            }
            catch (Exception error) when (error is IOException or TimeoutException or JsonException)
            {
                pipe.Dispose();
                continue;
            }
            if (op == OpFrame && answer.Str("evt") == "READY") return pipe;
            pipe.Dispose();
            throw new DiscordException($"Discord refused this application id ({answer.Str("message") ?? answer.ToJsonString()})");
        }
        throw new DiscordException("Discord isn't running on this computer");
    }

    private static void Write(Stream pipe, int op, JsonObject payload)
    {
        var data = Encoding.UTF8.GetBytes(payload.ToJsonString());
        var frame = new byte[8 + data.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), data.Length);
        data.CopyTo(frame, 8);
        pipe.Write(frame);
        pipe.Flush();
    }

    private static (int Op, JsonObject Data) Read(Stream pipe)
    {
        var header = new byte[8];
        pipe.ReadExactly(header);
        var data = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
        pipe.ReadExactly(data);
        var json = data.Length == 0 ? new JsonObject() : JsonNode.Parse(data) as JsonObject ?? new JsonObject();
        return (BinaryPrimitives.ReadInt32LittleEndian(header), json);
    }

    private void Send(string activity)
    {
        var payload = new JsonObject
        {
            ["cmd"] = "SET_ACTIVITY",
            ["nonce"] = Guid.NewGuid().ToString(),
            ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity == "null" ? null : JsonNode.Parse(activity) },
        };
        Write(_pipe!, OpFrame, payload);
        var (_, answer) = Read(_pipe!);
        if (answer.Str("evt") == "ERROR") throw new DiscordException($"Discord: {(answer["data"] as JsonObject)?.Str("message")}");
    }

    private void ClosePipe()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    private void Loop()
    {
        while (true)
        {
            _wake.WaitOne(Retry);
            var wanted = _wanted ?? "null";
            if (wanted == (_sent ?? "null")) continue;
            var wait = _lastSend + MinInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            wanted = _wanted ?? "null";
            var appId = ClientId;
            try
            {
                if (_sent == "reset" || _pipe is null || _pipeAppId != appId)
                {
                    ClosePipe();
                    _pipe = OpenPipe(appId);
                    _pipeAppId = appId;
                }
                Send(wanted);
                _sent = wanted;
                _lastSend = DateTime.UtcNow;
                Status = "connected";
            }
            catch (DiscordException error)
            {
                Status = error.Message;
                ClosePipe();
            }
            catch (Exception error) when (error is IOException or JsonException or ObjectDisposedException)
            {
                Status = "Discord was closed";          // reconnect on the next update / retry
                ClosePipe();
            }
        }
    }
}
