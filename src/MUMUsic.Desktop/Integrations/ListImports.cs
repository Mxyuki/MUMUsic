using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MUMUsic.Desktop.Library;
using MUMUsic.Desktop.UserData;

namespace MUMUsic.Desktop.Integrations;

/// <summary>A failure talking to AniList / VNDB, with the HTTP status the API answers with.</summary>
internal sealed class ImportException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Playlists from a user's AniList anime list (AMQ songs) or VNDB list (EMQ songs).</summary>
internal sealed partial class ListImports(UserStore user)
{
    // Watch statuses an AniList entry can have (custom lists only regroup entries, every entry has one of these).
    private static readonly string[] AniListStatuses = ["CURRENT", "COMPLETED", "REPEATING", "PAUSED", "PLANNING", "DROPPED"];
    private const string AniListQuery = """
        query ($user: String) { MediaListCollection(userName: $user, type: ANIME) {
            lists { entries { status media { id } } } } }
        """;
    // VNDB's built-in list labels (api.vndb.org/kana, "ulist_labels"); 6 = Blacklist is never offered.
    private static readonly int[] VndbLabels = [1, 2, 3, 4, 5, 7];
    private const int VndbPage = 100;          // the API's maximum page size

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", AppInfo.UserAgent }, { "Accept", "application/json" } },
    };

    [GeneratedRegex(@"^(https?://)?(www\.)?anilist\.co/user/", RegexOptions.IgnoreCase)]
    private static partial Regex AniListProfile();

    [GeneratedRegex(@"vndb\.org/(u\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VndbProfile();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{2,40}$")]
    private static partial Regex UserName();

    // ---------------------------------------------------------------- AniList

    /// <summary>Creates (or tops up) the playlist "AniList · &lt;user&gt;".</summary>
    public async Task<Dictionary<string, object?>> AniListAsync(MusicLibrary library, string userText, IReadOnlyCollection<string> statuses)
    {
        var userName = AniListProfile().Replace(userText.Trim().TrimEnd('/'), "").Split('/')[0];
        if (!UserName().IsMatch(userName)) throw new ImportException(400, "Enter an AniList user name or profile link");
        var wanted = statuses.Where(AniListStatuses.Contains).ToHashSet();
        if (wanted.Count == 0) wanted = ["CURRENT", "COMPLETED", "REPEATING"];

        var media = (await AniListEntries(userName)).Where(e => wanted.Contains(e.Status)).Select(e => e.MediaId).ToList();
        var anime = media.SelectMany(id => library.ByAniList.GetValueOrDefault(id) ?? []).ToList();
        var ids = SongsOf(library, anime, s => !s.Dub);
        return Fill($"AniList · {userName}", ids, media.Count, media.Count(library.ByAniList.ContainsKey));
    }

    private static async Task<List<(long MediaId, string Status)>> AniListEntries(string userName)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsJsonAsync(AppInfo.AniListApi, new { query = AniListQuery, variables = new { user = userName } });
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            throw new ImportException(502, $"Could not reach AniList: {error.Message}");
        }
        using (response)
        {
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject;
            if (!response.IsSuccessStatusCode)
            {
                var message = (body?["errors"] as JsonArray)?.FirstOrDefault()?["message"]?.GetValue<string>() ?? $"HTTP {(int)response.StatusCode}";
                if (message == "Private User") message = $"{userName}'s list is private";
                throw new ImportException((int)response.StatusCode < 500 ? 400 : 502, $"AniList: {message}");
            }
            var seen = new Dictionary<long, string>();
            foreach (var list in body?["data"]?["MediaListCollection"]?["lists"] as JsonArray ?? [])
            {
                foreach (var entry in list?["entries"] as JsonArray ?? [])     // an entry can sit in a status list and in custom lists too
                {
                    seen.TryAdd(entry!["media"]!["id"]!.GetValue<long>(), entry["status"]!.GetValue<string>());
                }
            }
            return seen.Select(e => (e.Key, e.Value)).ToList();
        }
    }

    // ---------------------------------------------------------------- VNDB

    /// <summary>Creates (or tops up) the playlist "VNDB · &lt;user&gt;".</summary>
    public async Task<Dictionary<string, object?>> VndbAsync(MusicLibrary library, string userText, IReadOnlyCollection<string> labels, bool withBgm)
    {
        var wanted = labels.Select(l => int.TryParse(l, out var id) ? id : 0).Where(VndbLabels.Contains).ToHashSet();
        if (wanted.Count == 0) wanted = [1, 2];
        var (userId, userName) = await FindVndbUser(userText);
        var vns = (await VndbEntries(userId)).Where(e => e.Labels.Overlaps(wanted)).Select(e => e.VnId).ToList();
        var works = vns.SelectMany(id => library.ByVndb.GetValueOrDefault(id) ?? []).ToList();
        var ids = SongsOf(library, works, s => withBgm || !s.Type.StartsWith("BGM", StringComparison.Ordinal));
        return Fill($"VNDB · {userName}", ids, vns.Count, vns.Count(library.ByVndb.ContainsKey));
    }

    private static async Task<JsonObject> VndbCall(HttpMethod method, string path, object? body = null)
    {
        HttpResponseMessage response;
        try
        {
            var request = new HttpRequestMessage(method, $"{AppInfo.VndbApi}/{path}");
            if (body is not null) request.Content = JsonContent.Create(body);
            response = await Http.SendAsync(request);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            throw new ImportException(502, $"Could not reach VNDB: {error.GetType().Name}");
        }
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode == 429) throw new ImportException(502, "VNDB: too many requests, try again in a few minutes");
            if (!response.IsSuccessStatusCode)
            {
                var message = text.Trim();
                throw new ImportException((int)response.StatusCode < 500 ? 400 : 502,
                    $"VNDB: {(message.Length > 0 ? message[..Math.Min(200, message.Length)] : $"HTTP {(int)response.StatusCode}")}");
            }
            return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
    }

    /// <summary>(user id "u123", user name) from a name, a user id or a profile / list link.</summary>
    private static async Task<(string Id, string Name)> FindVndbUser(string text)
    {
        text = text.Trim().TrimEnd('/');
        var match = VndbProfile().Match(text);
        var query = match.Success ? match.Groups[1].Value : text;
        if (!UserName().IsMatch(query)) throw new ImportException(400, "Enter a VNDB user name or profile link");
        var found = await VndbCall(HttpMethod.Get, $"user?q={Uri.EscapeDataString(query)}");
        if (found[query] is not JsonObject user) throw new ImportException(400, $"VNDB: no user named {query}");
        return (user.Str("id")!, user.Str("username")!);
    }

    /// <summary>(VN id "v17", label ids) of the publicly visible part of a list.</summary>
    private static async Task<List<(string VnId, HashSet<int> Labels)>> VndbEntries(string userId)
    {
        var entries = new List<(string, HashSet<int>)>();
        for (var page = 1; ; page++)
        {
            var data = await VndbCall(HttpMethod.Post, "ulist", new { user = userId, fields = "labels.id", sort = "id", results = VndbPage, page });
            foreach (var entry in data["results"] as JsonArray ?? [])
            {
                var labels = (entry!["labels"] as JsonArray ?? []).Select(l => l!["id"]!.GetValue<int>()).ToHashSet();
                entries.Add((entry["id"]!.GetValue<string>(), labels));
            }
            if (data.Bool("more") != true) return entries;
        }
    }

    // ---------------------------------------------------------------- shared

    /// <summary>Every playable song of the works, oldest work first, one version of each song (rebroadcasts, a song
    /// shared by two seasons).</summary>
    private static List<string> SongsOf(MusicLibrary library, IEnumerable<string> workIds, Func<Song, bool> keep)
    {
        var works = workIds.OrderBy(w => library.Works[w].Year ?? 9999).ThenBy(w => library.Works[w].Name.ToLowerInvariant(), StringComparer.Ordinal);
        var ids = works.SelectMany(library.WorkSongs).Where(i => !string.IsNullOrEmpty(library.Songs[i].File) && keep(library.Songs[i]));
        return library.Dedupe(ids);
    }

    private Dictionary<string, object?> Fill(string name, List<string> ids, int entries, int found)
    {
        var (id, created, added) = user.FillPlaylist(name, ids);
        return new()
        {
            ["id"] = id, ["name"] = name, ["created"] = created, ["entries"] = entries, ["found"] = found,
            ["songs"] = ids.Count, ["added"] = added,
        };
    }
}
