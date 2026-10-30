using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace MUMUsic.Desktop.Server;

/// <summary>One request from the window. Bodies are small JSON documents.</summary>
internal sealed class HttpRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required Dictionary<string, string> Query { get; init; }
    public required Dictionary<string, string> Headers { get; init; }
    public required byte[] Body { get; init; }

    public JsonObject Json()
    {
        if (Body.Length == 0) return [];
        return JsonNode.Parse(Body) as JsonObject ?? throw new FormatException("The request body is not a JSON object");
    }

    public string? Header(string name) => Headers.GetValueOrDefault(name);

    public bool IsWebSocket => string.Equals(Header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Writes one response on the connection.</summary>
internal sealed class HttpResponse(Stream stream)
{
    private static readonly Dictionary<int, string> Reasons = new()
    {
        [101] = "Switching Protocols", [200] = "OK", [206] = "Partial Content", [304] = "Not Modified", [400] = "Bad Request", [404] = "Not Found",
        [405] = "Method Not Allowed", [409] = "Conflict", [416] = "Range Not Satisfiable", [500] = "Internal Server Error",
        [502] = "Bad Gateway", [503] = "Service Unavailable",
    };

    public Stream Stream { get; } = stream;

    /// <summary>False when the response had no length, so the connection must close to end it.</summary>
    public bool KeepAlive { get; set; } = true;

    public async Task SendAsync(int status, byte[] body, string contentType = "application/json; charset=utf-8")
    {
        await HeadAsync(status, [("Content-Type", contentType), ("Content-Length", body.Length.ToString()), ("Cache-Control", "no-store")]);
        await Stream.WriteAsync(body);
    }

    public Task JsonAsync(int status, object? value) => SendAsync(status, MUMUsic.Desktop.Json.Bytes(value));

    /// <summary>Answers a WebSocket upgrade (RFC 6455); the connection then belongs to the socket.</summary>
    public async Task<WebSocket> AcceptWebSocketAsync(HttpRequest request)
    {
        const string handshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var key = request.Header("Sec-WebSocket-Key") ?? throw new FormatException("No Sec-WebSocket-Key");
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + handshakeGuid)));
        await HeadAsync(101, [("Upgrade", "websocket"), ("Connection", "Upgrade"), ("Sec-WebSocket-Accept", accept)]);
        KeepAlive = false;
        return WebSocket.CreateFromStream(Stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));
    }

    public async Task HeadAsync(int status, IEnumerable<(string Name, string Value)> headers)
    {
        var head = new StringBuilder($"HTTP/1.1 {status} {Reasons.GetValueOrDefault(status, "Status")}\r\n");
        foreach (var (name, value) in headers) head.Append(name).Append(": ").Append(value).Append("\r\n");
        if (!KeepAlive) head.Append("Connection: close\r\n");
        head.Append("\r\n");
        await Stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
    }
}

/// <summary>
/// A minimal HTTP/1.1 server on 127.0.0.1 for the window's page: the UI, the JSON API and the EMQ media stream.
/// Hand-written over a socket rather than HttpListener, so it doesn't depend on http.sys URL reservations. The port is
/// kept between runs (settings.json): the page's storage (theme, volume, queue) belongs to its origin.
/// </summary>
internal sealed class LocalServer(Func<HttpRequest, HttpResponse, Task> handle)
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 64 * 1024 * 1024;
    private TcpListener? _listener;

    public int Start(int? preferredPort)
    {
        _listener = TryListen(preferredPort ?? 0) ?? TryListen(0) ?? throw new IOException("Could not open a local port");
        _ = AcceptLoopAsync(_listener);
        return ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    private static TcpListener? TryListen(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        // Windows lets a second socket bind a port already in use unless this is set.
        listener.ExclusiveAddressUse = true;
        try
        {
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            var reader = new RequestReader(stream);
            try
            {
                while (await reader.ReadAsync() is { } request)
                {
                    var response = new HttpResponse(stream)
                    {
                        KeepAlive = !string.Equals(request.Header("Connection"), "close", StringComparison.OrdinalIgnoreCase),
                    };
                    await handle(request, response);
                    if (!response.KeepAlive) return;
                }
            }
            catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
            {
                // The page stopped reading (a seek, the next song): nothing to answer.
            }
            catch (FormatException)
            {
                // A malformed request line or headers.
            }
        }
    }

    /// <summary>Reads requests one after another from a kept-alive connection.</summary>
    private sealed class RequestReader(Stream stream)
    {
        private byte[] _buffer = new byte[8192];
        private int _start, _end;

        public async Task<HttpRequest?> ReadAsync()
        {
            int headerEnd;
            while ((headerEnd = HeaderEnd()) < 0)
            {
                if (_end - _start >= MaxHeaderBytes) throw new FormatException("Headers too large");
                if (!await FillAsync()) return null;
            }
            var head = Encoding.Latin1.GetString(_buffer, _start, headerEnd - _start);
            _start = headerEnd + 4;
            var lines = head.Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length < 3) throw new FormatException("Bad request line");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
            var length = headers.TryGetValue("Content-Length", out var text) && int.TryParse(text, out var n) ? n : 0;
            if (length is < 0 or > MaxBodyBytes) throw new FormatException("Bad body length");
            var body = new byte[length];
            var copied = 0;
            while (copied < length)
            {
                if (_start == _end && !await FillAsync()) throw new IOException("Connection closed in the body");
                var chunk = Math.Min(length - copied, _end - _start);
                Buffer.BlockCopy(_buffer, _start, body, copied, chunk);
                _start += chunk;
                copied += chunk;
            }
            var target = parts[1];
            var question = target.IndexOf('?');
            var path = question < 0 ? target : target[..question];
            return new HttpRequest
            {
                Method = parts[0].ToUpperInvariant(), Path = path, Query = ParseQuery(question < 0 ? "" : target[(question + 1)..]),
                Headers = headers, Body = body,
            };
        }

        private int HeaderEnd()
        {
            for (var i = _start; i + 3 < _end; i++)
            {
                if (_buffer[i] == '\r' && _buffer[i + 1] == '\n' && _buffer[i + 2] == '\r' && _buffer[i + 3] == '\n') return i;
            }
            return -1;
        }

        private async Task<bool> FillAsync()
        {
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            if (_end == _buffer.Length) Array.Resize(ref _buffer, _buffer.Length * 2);
            var read = await stream.ReadAsync(_buffer.AsMemory(_end));
            _end += read;
            return read > 0;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var values = new Dictionary<string, string>();
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
                var value = equals < 0 ? "" : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
                values.TryAdd(key, value);
            }
            return values;
        }
    }
}
