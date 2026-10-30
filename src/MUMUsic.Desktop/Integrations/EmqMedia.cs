using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace MUMUsic.Desktop.Integrations;

/// <summary>
/// EMQ song files, fetched with a guest session. erogemusicquiz.com only serves /selfhoststorage/ to requests carrying
/// the session cookies user-id + session-token (AuthController in github.com/mertvn/EMQ) and answers 500 without them.
/// A browser can't add those to a cross-site &lt;audio&gt; request, so the local server streams EMQ files through here.
/// The guest session is saved in the user folder and reused, so starting the app doesn't create a new guest each time.
/// </summary>
internal sealed class EmqMedia
{
    // an expired session gets 500 too
    private static readonly HttpStatusCode[] RetryStatuses = [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError];

    private readonly HttpClient _http = new(new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", AppInfo.UserAgent } },
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, string>? _cookies = Json.ReadFile<Dictionary<string, string>?>(AppPaths.EmqSessionFile, null);

    private async Task NewSessionAsync()
    {
        using var response = await _http.PostAsJsonAsync($"{AppInfo.EmqSite}/Auth/CreateSession",
            new { usernameOrEmail = "Guest", password = "", isGuest = true });
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<JsonObject>())?["session"]
                      ?? throw new HttpRequestException("EMQ gave no session");
        _cookies = new Dictionary<string, string>
        {
            ["user-id"] = session["player"]!["id"]!.ToJsonString(),
            ["session-token"] = session["token"]!.GetValue<string>(),
        };
        Json.WriteFile(AppPaths.EmqSessionFile, _cookies);
    }

    /// <summary>The response for one file, headers read and body not yet (Range passed through, so seeking works).</summary>
    public async Task<HttpResponseMessage> GetAsync(string fileName, string? range, CancellationToken cancel)
    {
        var url = fileName.StartsWith("http", StringComparison.Ordinal) ? fileName : AppInfo.EmqMedia + fileName;
        Dictionary<string, string>? used = null;
        for (var attempt = 0; ; attempt++)
        {
            await _gate.WaitAsync(cancel);
            try
            {
                // on a retry, renew only if no other request has renewed it meanwhile
                if (_cookies is null || (attempt > 0 && ReferenceEquals(_cookies, used))) await NewSessionAsync();
                used = _cookies;
            }
            finally
            {
                _gate.Release();
            }
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", string.Join("; ", used!.Select(c => $"{c.Key}={c.Value}")));
            if (range is not null) request.Headers.TryAddWithoutValidation("Range", range);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (attempt == 0 && RetryStatuses.Contains(response.StatusCode))
            {
                response.Dispose();
                continue;
            }
            return response;
        }
    }
}
