using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MUMUsic.Desktop.Library;

/// <summary>
/// The song library in memory, and everything computed from it: search, pages, similar songs, recommendations and
/// discovery (<c>MusicLibrary.Similarity.cs</c>).
///
/// Everything is loaded for both sources, so likes and playlists always resolve and a liked AMQ song can steer
/// recommendations while only EMQ is in use. The active sources (AMQ, EMQ or both, <see cref="SetSources"/>) only
/// limit what gets picked: search results, similar songs, recommendations, discovery, tag pages. Song kinds the user
/// turned off (insert songs, BGM...) are left out of what the app picks by itself, but still found by search and on
/// pages.
/// </summary>
internal sealed partial class MusicLibrary
{
    private const int FlagDub = 1, FlagRebroadcast = 2, FlagInstrumental = 4;
    private static readonly string[] TypeOrder = ["OP", "ED", "INS", "BGM"];
    public static readonly string[] Roles = ["perf", "comp", "arr", "lyr"];
    private static readonly Dictionary<string, string> RoleLabel = new()
    {
        ["perf"] = "Performed by", ["comp"] = "Composed by", ["arr"] = "Arranged by", ["lyr"] = "Lyrics by",
    };

    [GeneratedRegex(@"\s*(\([^)]*\)|\[[^\]]*\]|～[^～]*～|~[^~]*~|-[^-]*(ver|version|mix|edit)\.?-)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();

    public string Path { get; }
    public bool IsDev { get; }
    public Dictionary<string, string?> Meta { get; } = [];
    public Dictionary<string, Work> Works { get; } = [];
    public Dictionary<long, List<string>> ByAniList { get; } = [];
    public Dictionary<string, List<string>> ByVndb { get; } = [];
    public Dictionary<string, Person> People { get; } = [];
    public Dictionary<string, Song> Songs { get; } = [];
    public Dictionary<string, string> FileToId { get; } = [];

    // person -> every id of the same person / group (itself included); groups a person is in; a group's members
    private readonly Dictionary<string, HashSet<string>> _alias = [];
    private readonly Dictionary<string, HashSet<string>> _parents = [];
    private readonly Dictionary<string, List<string>> _members = [];
    private readonly List<string> _songList = [];
    private readonly Dictionary<string, Dictionary<string, List<string>>> _byRole = Roles.ToDictionary(r => r, _ => new Dictionary<string, List<string>>());
    private readonly Dictionary<string, List<string>> _byWork = [];

    // Tags as three flat arrays (song number, tag id, score) instead of a list per song; a song's tags are the slice
    // TagStart..TagEnd.
    private string[] _tagGroup = [];
    private string[] _tagName = [];
    private readonly Dictionary<string, int> _tagIdOf = [];
    private int[] _tagSong = [];
    private short[] _tagId = [];
    private float[] _tagScore = [];

    private ActiveSet _active = null!;

    /// <summary>What the active sources and song kinds allow, swapped as a whole so readers never see half of it.</summary>
    private sealed record ActiveSet(
        HashSet<string> Sources, HashSet<string> SkipKinds, bool[] Mask, List<string> ActiveIds, bool[] TagActive,
        int[] TagCount, List<string> Vocabulary);

    public MusicLibrary(string path, bool isDev, IEnumerable<string> sources, IEnumerable<string> skipKinds)
    {
        Path = path;
        IsDev = isDev;
        // No pooling: a pooled connection would keep the file open, and a library update replaces it.
        var connection = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using (var db = new SqliteConnection(connection.ToString()))
        {
            db.Open();
            foreach (var row in Query(db, "SELECT key, value FROM meta")) Meta[row.GetString(0)] = row.IsDBNull(1) ? null : row.GetString(1);
            LoadInfo(db);
            LoadSound(db);
        }
        SetSources(sources, skipKinds);
    }

    public IReadOnlyCollection<string> Sources => _active.Sources;
    public IReadOnlyCollection<string> SkipKinds => _active.SkipKinds;
    public int AnalysedCount => _embIds.Count;
    public int ActiveAnalysedCount => _active.ActiveIds.Count;

    public static string BaseTitle(string name)
    {
        var stripped = VersionPattern().Replace(name, " ").Trim().ToLowerInvariant();
        return stripped.Length > 0 ? stripped : name.ToLowerInvariant();
    }

    private static string SourceOf(string id) => id.StartsWith('e') ? "emq" : "amq";

    // ---------------------------------------------------------------- loading

    private void LoadInfo(SqliteConnection db)
    {
        const string workColumns = "id, kind, name, nameJa, altNames, year, category, genres, tags, score, aniListId, vndbId, developers";
        foreach (var r in Query(db, $"SELECT {workColumns} FROM works"))
        {
            var work = new Work
            {
                Id = r.GetString(0), Kind = r.GetString(1), Name = r.GetString(2), NameJa = Text(r, 3), Year = Long(r, 5),
                Category = Text(r, 6), Genres = StringList(Text(r, 7)), Tags = StringList(Text(r, 8)), Score = Double(r, 9),
                AniListId = Long(r, 10), VndbId = Text(r, 11), Developers = StringList(Text(r, 12)),
                Search = string.Join(' ', new[] { r.GetString(2), Text(r, 3), Text(r, 4) }.Where(s => !string.IsNullOrEmpty(s))).ToLowerInvariant(),
            };
            Works[work.Id] = work;
            if (work.AniListId is { } aniListId) Bucket(ByAniList, aniListId).Add(work.Id);   // a few map to 2-3 anime
            if (!string.IsNullOrEmpty(work.VndbId)) Bucket(ByVndb, work.VndbId).Add(work.Id);
        }

        var roots = new Dictionary<string, string>();
        foreach (var r in Query(db, "SELECT id, kind, name, COALESCE(alias, id) FROM people"))
        {
            var id = r.GetString(0);
            People[id] = new Person { Id = id, Kind = r.GetString(1), Name = r.GetString(2), Search = r.GetString(2).ToLowerInvariant() };
            roots[id] = r.GetString(3);
        }
        var clusters = new Dictionary<string, HashSet<string>>();
        foreach (var (id, root) in roots) Bucket(clusters, root).Add(id);
        foreach (var (id, root) in roots) _alias[id] = clusters[root];
        foreach (var r in Query(db, "SELECT groupId, memberId FROM members"))
        {
            Bucket(_parents, r.GetString(1)).Add(r.GetString(0));
            Bucket(_members, r.GetString(0)).Add(r.GetString(1));
        }

        var names = Query(db, "SELECT id, tagGroup, name FROM tagNames").Select(r => (Id: r.GetInt32(0), Group: r.GetString(1), Name: r.GetString(2))).ToList();
        var size = names.Count == 0 ? 0 : names.Max(n => n.Id) + 1;
        _tagGroup = Enumerable.Repeat("", size).ToArray();
        _tagName = Enumerable.Repeat("", size).ToArray();
        foreach (var (id, group, name) in names)
        {
            _tagGroup[id] = group;
            _tagName[id] = name;
            _tagIdOf[name] = id;
        }

        var tagSong = new List<int>();
        var tagId = new List<short>();
        var tagScore = new List<float>();
        const string songColumns = "id, workId, songKey, name, artist, type, flags, duration, volume, popularity, audio, video, performer, composer, arranger, lyricist, tags";
        foreach (var r in Query(db, $"SELECT {songColumns} FROM songs"))
        {
            var id = r.GetString(0);
            var work = Works.GetValueOrDefault(r.GetString(1));
            var name = r.GetString(3);
            var artist = r.GetString(4);
            var flags = r.GetInt64(6);
            var tags = TagPairs(Text(r, 16));
            var song = new Song
            {
                Id = id, Src = SourceOf(id), SongKey = r.GetString(2), Name = name, Artist = artist, WorkId = r.GetString(1),
                Work = work?.Name ?? "", WorkKind = work?.Kind, Year = work?.Year, Type = Text(r, 5) ?? "",
                Duration = Double(r, 7), Volume = Double(r, 8), Popularity = Long(r, 9),
                Dub = (flags & FlagDub) != 0, Rebroadcast = (flags & FlagRebroadcast) != 0, Instrumental = (flags & FlagInstrumental) != 0,
                File = Text(r, 10), VideoFile = Text(r, 11), Performer = Text(r, 12), Composer = Text(r, 13), Arranger = Text(r, 14),
                Lyricist = Text(r, 15), Search = $"{name} {artist}".ToLowerInvariant(),
                TitleKey = $"{name.ToLowerInvariant()}|{artist.ToLowerInvariant()}", BaseTitle = BaseTitle(name),
                TagStart = tagId.Count, TagEnd = tagId.Count + tags.Count,
            };
            foreach (var (tid, score) in tags)
            {
                tagSong.Add(_songList.Count);
                tagId.Add((short)tid);
                tagScore.Add(score);
            }
            Songs[id] = song;
            _songNumber[id] = _songList.Count;
            _songList.Add(id);
            Bucket(_byWork, song.WorkId).Add(id);
            foreach (var role in Roles)
            {
                if (song.Role(role) is { Length: > 0 } person) Bucket(_byRole[role], person).Add(id);
            }
            if (!string.IsNullOrEmpty(song.File)) FileToId[song.File] = id;
            if (!string.IsNullOrEmpty(song.VideoFile)) FileToId[song.VideoFile] = id;
        }
        _tagSong = [.. tagSong];
        _tagId = [.. tagId];
        _tagScore = [.. tagScore];

        foreach (var person in People.Values)
        {
            var cluster = _alias[person.Id];
            person.Count = cluster.Sum(i => _byRole["perf"].GetValueOrDefault(i)?.Count ?? 0);
            person.Credits = cluster.Sum(i => Roles.Sum(role => _byRole[role].GetValueOrDefault(i)?.Count ?? 0));
        }
    }

    private readonly Dictionary<string, int> _songNumber = [];

    /// <summary>Which libraries songs are picked from, minus the song kinds turned off. Instant: nothing is reloaded.</summary>
    public void SetSources(IEnumerable<string> sources, IEnumerable<string> skipKinds)
    {
        var sourceSet = sources.ToHashSet();
        var skipSet = skipKinds.ToHashSet();
        bool Picked(string id)
        {
            var song = Songs[id];
            return sourceSet.Contains(song.Src) && !skipSet.Contains(song.Kind);
        }
        var mask = _embIds.Select(Picked).ToArray();
        var activeIds = _embIds.Where((_, row) => mask[row]).ToList();
        var songActive = _songList.Select(Picked).ToArray();
        var tagActive = _tagSong.Select(n => songActive[n]).ToArray();
        var counts = new int[_tagName.Length];
        for (var i = 0; i < _tagId.Length; i++)
        {
            if (tagActive[i]) counts[_tagId[i]]++;
        }
        var vocabulary = Enumerable.Range(0, counts.Length).Where(t => counts[t] > 0).Select(t => _tagName[t])
            .Order(StringComparer.Ordinal).ToList();
        _active = new ActiveSet(sourceSet, skipSet, mask, activeIds, tagActive, counts, vocabulary);
    }

    /// <summary>Songs of each kind ({"amq:OP": 8839, ...}), for the settings.</summary>
    public Dictionary<string, int> KindCounts()
    {
        var counts = new Dictionary<string, int>();
        foreach (var song in Songs.Values) counts[song.Kind] = counts.GetValueOrDefault(song.Kind) + 1;
        return counts;
    }

    private bool IsActive(string id) => Songs.TryGetValue(id, out var song) && _active.Sources.Contains(song.Src);

    public int ActiveSongCount(string? source = null) =>
        Songs.Values.Count(s => _active.Sources.Contains(s.Src) && (source is null || s.Src == source));

    // ---------------------------------------------------------------- song views

    /// <summary>(audio, video, public link). EMQ only serves its files with session cookies, so they go through the
    /// local /media/ proxy (Server/EmqMedia.cs).</summary>
    private static (string? Audio, string? Video, string? Link) Urls(Song song)
    {
        string? Url(string? file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (song.Src == "amq") return AppInfo.AmqMedia + file;
            return file.StartsWith("http", StringComparison.Ordinal) ? file : "/media/" + QuotePath(file);
        }
        var link = string.IsNullOrEmpty(song.File) ? null : (song.Src == "amq" ? AppInfo.AmqMedia : AppInfo.EmqMedia) + song.File;
        return (Url(song.File), Url(song.VideoFile), link);
    }

    /// <summary>Percent-encodes everything but unreserved characters and "/" (like Python's urllib.parse.quote).</summary>
    public static string QuotePath(string text) => Uri.EscapeDataString(text).Replace("%2F", "/", StringComparison.Ordinal);

    public Dictionary<string, object?> SongView(string id, Dictionary<string, object?>? extra = null)
    {
        var song = Songs[id];
        var tags = Enumerable.Range(song.TagStart, song.TagEnd - song.TagStart).Select(i => _tagId[i]).ToList();
        var shortTags = tags.Where(t => _tagGroup[t] == "genre").Take(2)
            .Concat(tags.Where(t => _tagGroup[t] == "mood").Take(1)).Select(t => _tagName[t]).ToList();
        var (audio, video, link) = Urls(song);
        var view = new Dictionary<string, object?>
        {
            ["id"] = song.Id, ["src"] = song.Src, ["name"] = song.Name, ["artist"] = song.Artist, ["work"] = song.Work,
            ["workId"] = song.WorkId, ["workKind"] = song.WorkKind, ["year"] = song.Year, ["type"] = song.Type,
            ["dur"] = song.Duration, ["vol"] = song.Volume, ["audio"] = audio, ["video"] = video, ["link"] = link,
            ["tags"] = shortTags, ["analysed"] = _embIndex.ContainsKey(id),
            ["by"] = string.IsNullOrEmpty(song.Performer) ? song.Composer : song.Performer,
        };
        if (extra is not null)
        {
            foreach (var (key, value) in extra) view[key] = value;
        }
        return view;
    }

    public List<Dictionary<string, object?>> Views(IEnumerable<string> ids) =>
        ids.Where(Songs.ContainsKey).Select(id => SongView(id)).ToList();

    /// <summary>One version per song (prefer playable, original airing, analysed), in the order given.</summary>
    public List<string> Dedupe(IEnumerable<string> ids, IReadOnlySet<string>? skipKeys = null, IReadOnlySet<string>? skipTitles = null)
    {
        var best = new Dictionary<string, (int Rank, string Id)>();
        var order = new List<string>();
        foreach (var id in ids)
        {
            var song = Songs[id];
            if (skipKeys?.Contains(song.SongKey) == true || skipTitles?.Contains(song.TitleKey) == true) continue;
            var rank = (string.IsNullOrEmpty(song.File) ? 8 : 0) | (song.Dub ? 4 : 0) | (song.Rebroadcast ? 2 : 0)
                       | (_embIndex.ContainsKey(id) ? 0 : 1);
            if (!best.TryGetValue(song.TitleKey, out var current))
            {
                order.Add(song.TitleKey);
                best[song.TitleKey] = (rank, id);
            }
            else if (rank < current.Rank)
            {
                best[song.TitleKey] = (rank, id);
            }
        }
        return order.Select(key => best[key].Id).ToList();
    }

    private List<string> SortPopular(IEnumerable<string> ids) =>
        ids.OrderBy(i => Songs[i].Popularity is null).ThenBy(i => Songs[i].Popularity ?? 0).ThenBy(i => -(Songs[i].Year ?? 0)).ToList();

    public List<Dictionary<string, object?>> RandomPicks(int n = 16)
    {
        var pool = _active.ActiveIds;
        var rows = new HashSet<int>();
        while (rows.Count < Math.Min(n, pool.Count)) rows.Add(Random.Shared.Next(pool.Count));
        return Views(Dedupe(rows.Select(row => pool[row])));
    }

    // ---------------------------------------------------------------- search

    public Dictionary<string, object?> Search(string query, int limit = 80)
    {
        var words = query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new() { ["songs"] = Array.Empty<object>(), ["people"] = Array.Empty<object>(), ["works"] = Array.Empty<object>(), ["tags"] = Array.Empty<object>() };
        }
        var whole = string.Join(' ', words);
        var active = _active;
        var scored = new List<(int Score, long Pop, string Id)>();
        foreach (var song in Songs.Values)
        {
            if (!active.Sources.Contains(song.Src)) continue;
            var workSearch = Works.GetValueOrDefault(song.WorkId)?.Search ?? "";
            // Words hold no spaces, so none can span the song text and the work text.
            if (!words.All(w => song.Search.Contains(w, StringComparison.Ordinal) || workSearch.Contains(w, StringComparison.Ordinal))) continue;
            var name = song.Name.ToLowerInvariant();
            var score = name == whole ? 100 : name.StartsWith(whole, StringComparison.Ordinal) ? 60 : name.Contains(whole, StringComparison.Ordinal) ? 35 : 0;
            score += song.Artist.ToLowerInvariant().Contains(whole, StringComparison.Ordinal) ? 25 : 0;
            score += song.Popularity is < 2000 ? 3 : 0;
            scored.Add((-score, song.Popularity is null or 0 ? 1_000_000 : song.Popularity.Value, song.Id));
        }
        scored.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score)
            : a.Pop != b.Pop ? a.Pop.CompareTo(b.Pop) : string.CompareOrdinal(a.Id, b.Id));
        var songIds = Dedupe(scored.Select(s => s.Id)).Take(limit);

        var hits = People.Values.Where(p => p.Credits > 0 && active.Sources.Contains(SourceOf(p.Id)) && words.All(w => p.Search.Contains(w, StringComparison.Ordinal)))
            .OrderBy(p => p.Search != whole).ThenBy(p => !p.Search.StartsWith(whole, StringComparison.Ordinal)).ThenBy(p => -p.Credits);
        var people = new List<object>();
        var seen = new HashSet<HashSet<string>>(ReferenceEqualityComparer.Instance);
        foreach (var person in hits)
        {
            if (!seen.Add(_alias[person.Id])) continue;
            people.Add(new Dictionary<string, object?> { ["id"] = person.Id, ["name"] = person.Name, ["kind"] = person.Kind, ["count"] = person.Credits });
            if (people.Count == 12) break;
        }

        var works = Works.Values.Where(w => active.Sources.Contains(SourceOf(w.Id)) && words.All(word => w.Search.Contains(word, StringComparison.Ordinal)))
            .OrderBy(w => w.Name.ToLowerInvariant() != whole).ThenBy(w => !w.Name.ToLowerInvariant().StartsWith(whole, StringComparison.Ordinal))
            .ThenBy(w => -(w.Year ?? 0)).Take(12)
            .Select(w => new Dictionary<string, object?> { ["id"] = w.Id, ["kind"] = w.Kind, ["name"] = w.Name, ["year"] = w.Year });
        return new()
        {
            ["songs"] = Views(songIds),
            ["people"] = people,
            ["works"] = works.ToList(),
            ["tags"] = active.Vocabulary.Where(t => t.Contains(whole, StringComparison.Ordinal)).Take(10).ToList(),
        };
    }

    // ---------------------------------------------------------------- people, works, songs

    private string NameOf(string id) => People.GetValueOrDefault(id)?.Name ?? "?";

    /// <summary>Every group that (transitively) contains one of the given people. Group aliases are not followed:
    /// they are often other line-ups of the group.</summary>
    private HashSet<string> UpwardGroups(IEnumerable<string> nodes)
    {
        var seen = new HashSet<string>();
        var frontier = new Stack<string>(nodes);
        while (frontier.Count > 0)
        {
            foreach (var group in _parents.GetValueOrDefault(frontier.Pop()) ?? [])
            {
                if (seen.Add(group)) frontier.Push(group);
            }
        }
        return seen;
    }

    public Dictionary<string, object?>? PersonPage(string id)
    {
        if (!People.TryGetValue(id, out var person)) return null;
        var own = _alias[id];
        var via = UpwardGroups(own);
        via.ExceptWith(own);
        var withGroups = own.Union(via).ToList();

        List<Dictionary<string, object?>> Collect(string role, IEnumerable<string> people)
        {
            var ids = people.SelectMany(x => _byRole[role].GetValueOrDefault(x) ?? []).Where(IsActive).ToHashSet();
            return Views(Dedupe(SortPopular(ids)));
        }

        var groups = own.SelectMany(x => _parents.GetValueOrDefault(x) ?? []).Distinct()
            .Select(g => new { Id = g, Count = People.GetValueOrDefault(g)?.Count ?? 0 })
            .OrderBy(g => -g.Count).Take(40)
            .Select(g => new Dictionary<string, object?> { ["id"] = g.Id, ["name"] = NameOf(g.Id), ["count"] = g.Count }).ToList();
        var page = new Dictionary<string, object?>
        {
            ["id"] = id, ["kind"] = person.Kind, ["name"] = person.Name,
            ["aliases"] = own.Select(NameOf).Where(n => n != person.Name).Distinct().Order(StringComparer.Ordinal).ToList(),
            ["groups"] = groups,
            ["songs"] = Collect("perf", withGroups),
            ["composed"] = Collect("comp", withGroups),
            ["arranged"] = Collect("arr", withGroups),
            ["lyrics"] = Collect("lyr", own),
        };
        var members = own.SelectMany(g => _members.GetValueOrDefault(g) ?? []).Distinct().ToList();
        if (members.Count > 0)
        {
            page["members"] = members.Select(m => new Dictionary<string, object?> { ["id"] = m, ["name"] = NameOf(m) })
                .OrderBy(m => ((string)m["name"]!).ToLowerInvariant(), StringComparer.Ordinal).ToList();
        }
        return page;
    }

    /// <summary>OPs, then EDs, insert songs, BGM.</summary>
    public List<string> WorkSongs(string workId)
    {
        int Order(Song s)
        {
            var index = Array.IndexOf(TypeOrder, s.Type.Split(' ')[0]);
            return index < 0 ? TypeOrder.Length : index;
        }
        return (_byWork.GetValueOrDefault(workId) ?? []).Select(id => Songs[id])
            .OrderBy(Order).ThenBy(s => s.Type, StringComparer.Ordinal).ThenBy(s => s.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .Select(s => s.Id).ToList();
    }

    public Dictionary<string, object?>? WorkPage(string workId)
    {
        if (!Works.TryGetValue(workId, out var w)) return null;
        return new()
        {
            ["id"] = w.Id, ["kind"] = w.Kind, ["name"] = w.Name, ["nameJa"] = w.NameJa, ["year"] = w.Year, ["category"] = w.Category,
            ["genres"] = w.Genres, ["tags"] = w.Tags, ["score"] = w.Score, ["vndbId"] = w.VndbId, ["developers"] = w.Developers,
            ["songs"] = Views(WorkSongs(workId)),
        };
    }

    /// <summary>The anime's AniList page / the VN's VNDB page (a search there when the library has no id).</summary>
    public string? WorkSite(string workId)
    {
        if (!Works.TryGetValue(workId, out var w)) return null;
        if (w.Kind == "vn")
        {
            return string.IsNullOrEmpty(w.VndbId) ? $"{AppInfo.VndbSite}/v?q={Uri.EscapeDataString(w.Name)}" : $"{AppInfo.VndbSite}/{w.VndbId}";
        }
        return w.AniListId is { } id ? $"{AppInfo.AniListSite}/anime/{id}" : $"{AppInfo.AniListSite}/search/anime?search={Uri.EscapeDataString(w.Name)}";
    }

    public Dictionary<string, object?>? SongDetail(string id)
    {
        if (!Songs.TryGetValue(id, out var song)) return null;
        var credits = Roles.Where(role => !string.IsNullOrEmpty(song.Role(role))).Select(role => new Dictionary<string, object?>
        {
            ["role"] = RoleLabel[role], ["id"] = song.Role(role), ["kind"] = People.GetValueOrDefault(song.Role(role)!)?.Kind ?? "artist",
            ["name"] = NameOf(song.Role(role)!),
        }).ToList();
        var work = Works.GetValueOrDefault(song.WorkId);
        var fullTags = Enumerable.Range(song.TagStart, song.TagEnd - song.TagStart).Select(i => new Dictionary<string, object?>
        {
            ["group"] = _tagGroup[_tagId[i]], ["tag"] = _tagName[_tagId[i]], ["score"] = Math.Round(_tagScore[i], 3),
        }).ToList();
        return SongView(id, new()
        {
            ["fullTags"] = fullTags, ["credits"] = credits, ["workGenres"] = work?.Genres ?? [], ["workCategory"] = work?.Category,
            ["features"] = FeatureView(id), ["file"] = song.File,
        });
    }

    public Dictionary<string, object?> TagPage(string tag)
    {
        var active = _active;
        var positions = new List<int>();
        if (_tagIdOf.TryGetValue(tag, out var tid))
        {
            for (var i = 0; i < _tagId.Length; i++)
            {
                if (_tagId[i] == tid && active.TagActive[i]) positions.Add(i);
            }
        }
        var ids = positions.OrderBy(i => -_tagScore[i]).Select(i => _songList[_tagSong[i]]);
        return new() { ["tag"] = tag, ["songs"] = Views(Dedupe(ids).Take(400)) };
    }

    public List<Dictionary<string, object?>> TagCounts()
    {
        var counts = _active.TagCount;
        return Enumerable.Range(0, counts.Length).Where(t => counts[t] > 0).OrderByDescending(t => counts[t])
            .Select(t => new Dictionary<string, object?> { ["tag"] = _tagName[t], ["group"] = _tagGroup[t], ["count"] = counts[t] }).ToList();
    }

    /// <summary>A song id, a song file name or a link to it (AMQ or EMQ) -> song id.</summary>
    public string? Resolve(string reference)
    {
        reference = reference.Trim().Split('?')[0].TrimEnd('/');
        if (Songs.ContainsKey(reference)) return reference;
        var marker = new[] { "/selfhoststorage/", "/media/" }.FirstOrDefault(m => reference.Contains(m, StringComparison.Ordinal));
        reference = marker is not null ? reference[(reference.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]
            : reference[(reference.LastIndexOf('/') + 1)..];
        return FileToId.GetValueOrDefault(Uri.UnescapeDataString(reference));
    }

    // ---------------------------------------------------------------- sqlite / json helpers

    private static IEnumerable<SqliteDataReader> Query(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read()) yield return reader;
    }

    private static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long? Long(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    private static double? Double(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private static List<TValue> Bucket<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        return list;
    }

    private static HashSet<TValue> Bucket<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> map, TKey key) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var set)) map[key] = set = [];
        return set;
    }

    private static List<string> StringList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<JsonElement>>(json)?.Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString()).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<(int Id, float Score)> TagPairs(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<List<double>>>(json)?.Where(p => p.Count >= 2).Select(p => ((int)p[0], (float)p[1])).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
