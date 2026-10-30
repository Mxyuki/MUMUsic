using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MUMUsic.Desktop.Library;

namespace MUMUsic.Desktop.Integrations;

/// <summary>A work's picture for the big view of a song without a clip.</summary>
/// <param name="Url">The image on AniList's or VNDB's servers.</param>
/// <param name="Nsfw">VNDB rates the image explicit, or AniList marks the anime adult: shown blurred until the user
/// chooses otherwise.</param>
internal sealed record WorkPicture(string Url, bool Nsfw);

/// <summary>
/// Pictures of anime (AniList cover) and visual novels (VNDB cover). VNs have no banner on VNDB, the cover is the image
/// it shows for them. Found once per work and kept in art-cache.json in the user folder; a work without a picture is
/// asked again at the next start.
/// </summary>
internal sealed class WorkArt
{
    // VNDB's image ratings are averages of votes: 0 safe, 1 suggestive, 2 explicit.
    private const double ExplicitRating = 1.3;
    private const string AniListQuery = "query ($id: Int) { Media(id: $id, type: ANIME) { isAdult coverImage { extraLarge large } } }";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", AppInfo.UserAgent }, { "Accept", "application/json" } },
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, WorkPicture> _found = Json.ReadFile(AppPaths.ArtCacheFile, new Dictionary<string, WorkPicture>());
    private readonly HashSet<string> _missing = [];

    public async Task<WorkPicture?> GetAsync(Work work)
    {
        var key = work.Kind == "vn" ? work.VndbId : work.AniListId?.ToString();
        if (string.IsNullOrEmpty(key)) return null;
        key = $"{work.Kind}:{key}";
        lock (_gate)
        {
            if (_found.TryGetValue(key, out var known)) return known;
            if (_missing.Contains(key)) return null;
        }
        WorkPicture? picture;
        try
        {
            picture = work.Kind == "vn" ? await FromVndb(work.VndbId!) : await FromAniList(work.AniListId!.Value);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log.Error($"art {key}", error);
            return null;            // a network error: asked again next time
        }
        lock (_gate)
        {
            if (picture is null)
            {
                _missing.Add(key);
                return null;
            }
            _found[key] = picture;
            Json.WriteFile(AppPaths.ArtCacheFile, _found);
        }
        return picture;
    }

    private static async Task<WorkPicture?> FromAniList(long id)
    {
        using var response = await Http.PostAsJsonAsync(AppInfo.AniListApi, new { query = AniListQuery, variables = new { id } });
        if (!response.IsSuccessStatusCode) return null;
        var media = (await response.Content.ReadFromJsonAsync<JsonObject>())?["data"]?["Media"] as JsonObject;
        var cover = media?["coverImage"] as JsonObject;
        var url = cover?.Str("extraLarge") ?? cover?.Str("large");
        return url is null ? null : new WorkPicture(url, media!.Bool("isAdult") == true);
    }

    private static async Task<WorkPicture?> FromVndb(string id)
    {
        using var response = await Http.PostAsJsonAsync($"{AppInfo.VndbApi}/vn",
            new { filters = new object[] { "id", "=", id }, fields = "image.url, image.sexual, image.violence" });
        if (!response.IsSuccessStatusCode) return null;
        var results = (await response.Content.ReadFromJsonAsync<JsonObject>())?["results"] as JsonArray;
        var image = results?.FirstOrDefault()?["image"] as JsonObject;
        var url = image?.Str("url");
        if (url is null) return null;
        var sexual = image!["sexual"]?.GetValue<double>() ?? 0;
        var violence = image["violence"]?.GetValue<double>() ?? 0;
        return new WorkPicture(url, sexual >= ExplicitRating || violence >= ExplicitRating);
    }
}
