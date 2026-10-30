namespace MUMUsic.Desktop.Library;

/// <summary>One song of the library, as read from library.db. Ids carry their source: "a123" (AMQ), "e456" (EMQ).</summary>
internal sealed class Song
{
    public required string Id { get; init; }
    public required string Src { get; init; }
    public required string SongKey { get; init; }
    public required string Name { get; init; }
    public required string Artist { get; init; }
    public required string WorkId { get; init; }
    public required string Work { get; init; }
    public string? WorkKind { get; init; }
    public long? Year { get; init; }
    public required string Type { get; init; }
    public double? Duration { get; init; }
    public double? Volume { get; init; }
    public long? Popularity { get; init; }
    public bool Dub { get; init; }
    public bool Rebroadcast { get; init; }
    public bool Instrumental { get; init; }
    public string? File { get; init; }
    public string? VideoFile { get; init; }
    public string? Performer { get; init; }
    public string? Composer { get; init; }
    public string? Arranger { get; init; }
    public string? Lyricist { get; init; }

    /// <summary>"name artist", lower case.</summary>
    public required string Search { get; init; }
    /// <summary>"name|artist", lower case: two versions of the same song share it.</summary>
    public required string TitleKey { get; init; }
    /// <summary>The title without "(Acoustic Version)", "-Orchestra ver.-"...: covers and remixes share it.</summary>
    public required string BaseTitle { get; init; }
    /// <summary>This song's slice of the flat tag arrays.</summary>
    public int TagStart { get; init; }
    public int TagEnd { get; init; }

    /// <summary>"amq:OP", "emq:BGM"... (<see cref="AppInfo.SongKinds"/>).</summary>
    public string Kind
    {
        get
        {
            var first = Type.Split(' ')[0];
            return $"{Src}:{(first.Length > 0 ? first : "OTHER")}";
        }
    }

    public string? Role(string role) => role switch
    {
        "perf" => Performer,
        "comp" => Composer,
        "arr" => Arranger,
        "lyr" => Lyricist,
        _ => null,
    };
}

/// <summary>An anime or a visual novel.</summary>
internal sealed class Work
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public string? NameJa { get; init; }
    public long? Year { get; init; }
    public string? Category { get; init; }
    public required List<string> Genres { get; init; }
    public required List<string> Tags { get; init; }
    public double? Score { get; init; }
    public long? AniListId { get; init; }
    public string? VndbId { get; init; }
    public required List<string> Developers { get; init; }
    public required string Search { get; init; }
}

/// <summary>An artist or a group. Ids: "a" AMQ artist, "g" AMQ group, "e" EMQ artist.</summary>
internal sealed class Person
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string Search { get; init; }
    /// <summary>Songs performed (aliases included) / credits of any role.</summary>
    public int Count { get; set; }
    public int Credits { get; set; }
}
