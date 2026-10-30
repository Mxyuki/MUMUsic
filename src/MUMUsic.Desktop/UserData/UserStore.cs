using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using MUMUsic.Desktop.Library;

namespace MUMUsic.Desktop.UserData;

/// <summary>The user's own data in user.db: likes, playlists and play history, plus JSON export / import. Song ids are
/// the library ids ("a123", "e456"). Same schema as the 1.x app.</summary>
internal sealed class UserStore
{
    private const int KeepBackups = 10;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS likes (
            songId  TEXT PRIMARY KEY,
            likedAt TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS playlists (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            name      TEXT NOT NULL,
            createdAt TEXT,
            updatedAt TEXT
        );
        CREATE TABLE IF NOT EXISTS playlistSongs (
            playlistId INTEGER NOT NULL,
            songId     TEXT    NOT NULL,
            position   REAL    NOT NULL,
            addedAt    TEXT,
            PRIMARY KEY (playlistId, songId)
        );
        CREATE TABLE IF NOT EXISTS plays (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            songId   TEXT NOT NULL,
            playedAt TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_plays_song ON plays(songId);
        """;

    private readonly string _path;
    private readonly string _connectionString;

    public UserStore(string path)
    {
        _path = path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 30 }.ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var db = Open();
        Execute(db, "PRAGMA journal_mode=WAL");
        Execute(db, Schema);
    }

    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'");

    /// <summary>user.backup.db at every start; backups/user-&lt;time&gt;-&lt;reason&gt;.db before an update (the last 10
    /// are kept). SQLite's backup API, so what is still in the -wal file is included.</summary>
    public void Backup(string? reason = null)
    {
        if (!File.Exists(_path)) return;
        string destination;
        if (reason is null)
        {
            destination = Path.Combine(Path.GetDirectoryName(_path)!, "user.backup.db");
        }
        else
        {
            Directory.CreateDirectory(AppPaths.BackupDir);
            destination = Path.Combine(AppPaths.BackupDir, $"user-{DateTime.Now:yyyyMMdd-HHmmss}-{reason}.db");
        }
        using (var source = Open())
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = destination, Pooling = false }.ToString()))
        {
            target.Open();
            source.BackupDatabase(target);
        }
        if (reason is null) return;
        foreach (var old in Directory.GetFiles(AppPaths.BackupDir, "user-*.db").Order(StringComparer.Ordinal)
                     .SkipLast(KeepBackups))
        {
            File.Delete(old);
        }
    }

    // ---------------------------------------------------------------- likes, plays

    public List<string> LikedIds()
    {
        using var db = Open();
        return Strings(db, "SELECT songId FROM likes ORDER BY likedAt DESC");
    }

    public bool IsLiked(string songId)
    {
        using var db = Open();
        return Scalar(db, "SELECT 1 FROM likes WHERE songId = $a", songId) is not null;
    }

    public void Like(string songId, bool liked)
    {
        using var db = Open();
        if (liked) Execute(db, "INSERT OR IGNORE INTO likes VALUES ($a, $b)", songId, Now());
        else Execute(db, "DELETE FROM likes WHERE songId = $a", songId);
    }

    public void AddPlay(string songId)
    {
        using var db = Open();
        Execute(db, "INSERT INTO plays (songId, playedAt) VALUES ($a, $b)", songId, Now());
    }

    public List<string> RecentIds(int limit)
    {
        using var db = Open();
        return Strings(db, "SELECT songId FROM plays GROUP BY songId ORDER BY MAX(id) DESC LIMIT $a", limit);
    }

    // ---------------------------------------------------------------- playlists

    public List<Dictionary<string, object?>> Playlists()
    {
        using var db = Open();
        return Rows(db, """
            SELECT p.id, p.name, p.createdAt, p.updatedAt, COUNT(ps.songId) AS count
            FROM playlists p LEFT JOIN playlistSongs ps ON ps.playlistId = p.id
            GROUP BY p.id ORDER BY p.updatedAt DESC
            """);
    }

    public Dictionary<string, object?>? Playlist(long id)
    {
        using var db = Open();
        return Rows(db, "SELECT * FROM playlists WHERE id = $a", id).FirstOrDefault();
    }

    public List<string> PlaylistSongIds(long id)
    {
        using var db = Open();
        return Strings(db, "SELECT songId FROM playlistSongs WHERE playlistId = $a ORDER BY position", id);
    }

    public long? FindPlaylist(string name)
    {
        using var db = Open();
        return Scalar(db, "SELECT id FROM playlists WHERE name = $a", name) is long id ? id : null;
    }

    public long CreatePlaylist(string name, IEnumerable<string> songIds, string? createdAt = null)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Execute(db, "INSERT INTO playlists (name, createdAt, updatedAt) VALUES ($a, $b, $c)", name, createdAt ?? Now(), Now());
        var id = (long)Scalar(db, "SELECT last_insert_rowid()")!;
        Add(db, id, songIds);
        transaction.Commit();
        return id;
    }

    public int AddToPlaylist(long id, IEnumerable<string> songIds)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        var added = Add(db, id, songIds);
        transaction.Commit();
        return added;
    }

    /// <summary>Creates the playlist, or adds to the one of that name what it misses.</summary>
    public (long Id, bool Created, int Added) FillPlaylist(string name, IReadOnlyCollection<string> songIds)
    {
        if (FindPlaylist(name) is { } id) return (id, false, AddToPlaylist(id, songIds));
        return (CreatePlaylist(name, songIds), true, songIds.Count);
    }

    public void RenamePlaylist(long id, string name)
    {
        using var db = Open();
        Execute(db, "UPDATE playlists SET name = $a, updatedAt = $b WHERE id = $c", name, Now(), id);
    }

    public void DeletePlaylist(long id)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Execute(db, "DELETE FROM playlistSongs WHERE playlistId = $a", id);
        Execute(db, "DELETE FROM playlists WHERE id = $a", id);
        transaction.Commit();
    }

    public void RemoveFromPlaylist(long id, string songId)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        Execute(db, "DELETE FROM playlistSongs WHERE playlistId = $a AND songId = $b", id, songId);
        Touch(db, id);
        transaction.Commit();
    }

    public void ReorderPlaylist(long id, IReadOnlyList<string> songIds)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        for (var position = 0; position < songIds.Count; position++)
        {
            Execute(db, "UPDATE playlistSongs SET position = $a WHERE playlistId = $b AND songId = $c",
                (double)position, id, songIds[position]);
        }
        Touch(db, id);
        transaction.Commit();
    }

    private static int Add(SqliteConnection db, long id, IEnumerable<string> songIds)
    {
        var position = Convert.ToDouble(Scalar(db,
            "SELECT COALESCE(MAX(position), 0) FROM playlistSongs WHERE playlistId = $a", id));
        var added = 0;
        foreach (var songId in songIds)
        {
            position += 1;
            added += Execute(db, "INSERT OR IGNORE INTO playlistSongs VALUES ($a, $b, $c, $d)", id, songId, position, Now());
        }
        Touch(db, id);
        return added;
    }

    private static void Touch(SqliteConnection db, long id) =>
        Execute(db, "UPDATE playlists SET updatedAt = $a WHERE id = $b", Now(), id);

    // ---------------------------------------------------------------- export / import

    public Dictionary<string, object?> Export(MusicLibrary library)
    {
        Dictionary<string, object?> Ref(string id)
        {
            var song = library.Songs.GetValueOrDefault(id);
            return new() { ["id"] = id, ["file"] = song?.File, ["name"] = song?.Name, ["artist"] = song?.Artist, ["work"] = song?.Work };
        }

        List<Dictionary<string, object?>> likes;
        using (var db = Open())
        {
            likes = Rows(db, "SELECT songId, likedAt FROM likes ORDER BY likedAt").Select(row =>
            {
                var item = Ref((string)row["songId"]!);
                item["likedAt"] = row["likedAt"];
                return item;
            }).ToList();
        }
        return new()
        {
            ["app"] = AppInfo.Name,
            ["version"] = 2,
            ["exportedAt"] = Now(),
            ["likes"] = likes,
            ["playlists"] = Playlists().Select(p => new Dictionary<string, object?>
            {
                ["name"] = p["name"], ["createdAt"] = p["createdAt"],
                ["songs"] = PlaylistSongIds((long)p["id"]!).Select(Ref).ToList(),
            }).ToList(),
        };
    }

    /// <summary>Merges an export file. Also reads the exports of the older AMQ / EMQ players, whose ids were plain
    /// numbers: songs are then found by file name, else by title + artist.</summary>
    public Dictionary<string, int> Import(MusicLibrary library, JsonNode? data)
    {
        if (data is not JsonObject root || (root["likes"] is null && root["playlists"] is null))
        {
            throw new FormatException("Not a MUMUsic export file");
        }
        var byTitle = new Dictionary<string, string>();
        foreach (var song in library.Songs.Values) byTitle.TryAdd(song.TitleKey, song.Id);

        string? Resolve(JsonNode? reference)
        {
            if (reference is not JsonObject item)
            {
                var plain = Json.Text(reference);
                return plain is not null && library.Songs.ContainsKey(plain) ? plain : null;
            }
            if (item.Str("id") is { } id && library.Songs.ContainsKey(id)) return id;
            if (item.Str("file") is { } file && library.FileToId.TryGetValue(file, out var byFile)) return byFile;
            var key = $"{(item.Str("name") ?? "").ToLowerInvariant()}|{(item.Str("artist") ?? "").ToLowerInvariant()}";
            return byTitle.GetValueOrDefault(key);
        }

        var stats = new Dictionary<string, int> { ["likes"] = 0, ["playlists"] = 0, ["songs"] = 0, ["missing"] = 0 };
        using (var db = Open())
        using (var transaction = db.BeginTransaction())
        {
            foreach (var reference in root["likes"] as JsonArray ?? [])
            {
                if (Resolve(reference) is not { } songId)
                {
                    stats["missing"]++;
                    continue;
                }
                var likedAt = (reference as JsonObject)?.Str("likedAt");
                stats["likes"] += Execute(db, "INSERT OR IGNORE INTO likes VALUES ($a, $b)", songId, likedAt ?? Now());
            }
            transaction.Commit();
        }
        foreach (var playlist in (root["playlists"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var ids = new List<string>();
            foreach (var reference in playlist["songs"] as JsonArray ?? [])
            {
                if (Resolve(reference) is { } songId) ids.Add(songId);
                else stats["missing"]++;
            }
            var name = (playlist.Str("name") ?? "").Trim();
            if (name.Length == 0) name = "Imported playlist";
            if (FindPlaylist(name) is { } existing)
            {
                stats["songs"] += AddToPlaylist(existing, ids);
                continue;
            }
            CreatePlaylist(name, ids, playlist.Str("createdAt"));
            stats["playlists"]++;
            stats["songs"] += ids.Distinct().Count();
        }
        return stats;
    }

    // ---------------------------------------------------------------- sqlite helpers

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    private static SqliteCommand Command(SqliteConnection db, string sql, object?[] values)
    {
        var command = db.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$" + (char)('a' + i), values[i] ?? DBNull.Value);
        return command;
    }

    private static int Execute(SqliteConnection db, string sql, params object?[] values)
    {
        using var command = Command(db, sql, values);
        return command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection db, string sql, params object?[] values)
    {
        using var command = Command(db, sql, values);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    private static List<string> Strings(SqliteConnection db, string sql, params object?[] values)
    {
        using var command = Command(db, sql, values);
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    private static List<Dictionary<string, object?>> Rows(SqliteConnection db, string sql, params object?[] values)
    {
        using var command = Command(db, sql, values);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
}
