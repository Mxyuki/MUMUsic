using Microsoft.Data.Sqlite;

namespace MUMUsic.Desktop.Library;

/// <summary>Sound vectors, measured features and tag vectors, and what is computed from them: similar songs,
/// recommendations from likes and the next discovery song. The weights come from the AMQ benchmark of the 1.x app.</summary>
internal sealed partial class MusicLibrary
{
    // How much each tag group counts when comparing the "vibe" of two songs (on top of idf: rare tags weigh more).
    private static readonly Dictionary<string, float> VibeGroupWeight = new()
    {
        ["genre"] = 1.0f, ["mood"] = 0.8f, ["vocals"] = 0.5f, ["instruments"] = 0.5f, ["tempo"] = 0.4f,
    };

    private SoundVectors _emb = null!;
    private List<string> _embIds = [];
    private readonly Dictionary<string, int> _embIndex = [];

    // Per vector row: raw bpm / loudness / brightness / punch (NaN when unknown); log2 bpm; z-scores of loudness,
    // log brightness and punch; each column sorted, for a song's percentiles.
    private double[,] _featRaw = new double[0, 4];
    private double[] _logBpm = [];
    private double[,] _featZ = new double[0, 3];
    private readonly Dictionary<string, double[]> _featSorted = [];
    private readonly Dictionary<string, string?> _musicKey = [];

    // idf-weighted unit tag vector per vector row (dense: 63 tags)
    private float[] _tagVectors = [];
    private int _tagDims;

    private void LoadSound(SqliteConnection db)
    {
        var dims = int.Parse(Meta.GetValueOrDefault("vectorDims") ?? "0");
        var count = Query(db, "SELECT COUNT(*) FROM vectors").Select(r => r.GetInt32(0)).First();
        var values = new sbyte[count * dims];
        foreach (var r in Query(db, "SELECT songId, v FROM vectors ORDER BY rowid"))
        {
            var row = _embIds.Count;
            if (row == count) break;
            _embIndex[r.GetString(0)] = row;
            _embIds.Add(r.GetString(0));
            r.GetBytes(1, 0, (byte[])(Array)values, row * dims, dims);
        }
        _emb = new SoundVectors(values, _embIds.Count, dims);

        var features = new Dictionary<string, double[]>();
        foreach (var r in Query(db, "SELECT id, bpm, loudness, brightness, punch, musicKey FROM songs WHERE bpm IS NOT NULL"))
        {
            features[r.GetString(0)] = [.. Enumerable.Range(1, 4).Select(i => r.IsDBNull(i) ? double.NaN : r.GetDouble(i))];
            _musicKey[r.GetString(0)] = Text(r, 5);
        }
        FeatureArrays(features);
        TagVectors();
    }

    private void FeatureArrays(Dictionary<string, double[]> features)
    {
        var n = _embIds.Count;
        _featRaw = new double[n, 4];
        var a = new double[n, 4];
        for (var row = 0; row < n; row++)
        {
            var values = features.GetValueOrDefault(_embIds[row]);
            for (var k = 0; k < 4; k++) _featRaw[row, k] = a[row, k] = values?[k] ?? double.NaN;
            a[row, 0] = Math.Log2(a[row, 0]);
            a[row, 2] = Math.Log(a[row, 2]);
        }
        _logBpm = new double[n];
        for (var row = 0; row < n; row++) _logBpm[row] = a[row, 0];
        _featZ = new double[n, 3];
        for (var k = 1; k < 4; k++)
        {
            var column = Enumerable.Range(0, n).Select(row => a[row, k]).Where(v => !double.IsNaN(v)).ToArray();
            var mean = column.Length > 0 ? column.Average() : double.NaN;
            var std = column.Length > 0 ? Math.Sqrt(column.Sum(v => (v - mean) * (v - mean)) / column.Length) : double.NaN;
            for (var row = 0; row < n; row++) _featZ[row, k - 1] = (a[row, k] - mean) / (std + 1e-9);
        }
        string[] names = ["bpm", "loudness", "brightness", "punch"];
        for (var k = 0; k < 4; k++)
        {
            var column = Enumerable.Range(0, n).Select(row => a[row, k]).Where(v => !double.IsNaN(v)).ToArray();
            Array.Sort(column);
            _featSorted[names[k]] = column;
        }
    }

    private Dictionary<string, object?>? FeatureView(string id)
    {
        if (!_embIndex.TryGetValue(id, out var row) || double.IsNaN(_featRaw[row, 0])) return null;
        double? Percentile(string name, double value)
        {
            var sorted = _featSorted[name];
            if (sorted.Length == 0 || double.IsNaN(value)) return null;
            return Math.Round((double)LowerBound(sorted, value) / sorted.Length, 3);
        }
        var brightness = _featRaw[row, 2];
        return new()
        {
            ["bpm"] = Math.Round(_featRaw[row, 0], MidpointRounding.ToEven),
            ["key"] = _musicKey.GetValueOrDefault(id),
            ["loudness"] = Percentile("loudness", _featRaw[row, 1]),
            ["brightness"] = brightness > 0 ? Percentile("brightness", Math.Log(brightness)) : null,
            ["punch"] = Percentile("punch", _featRaw[row, 3]),
        };
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int low = 0, high = sorted.Length;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sorted[mid] < value) low = mid + 1;
            else high = mid;
        }
        return low;
    }

    /// <summary>How close songs are in tempo (half / double time counts as close), loudness, brightness and punch.</summary>
    private double[] FeatureSimilarity(int[] rows, int reference)
    {
        var output = new double[rows.Length];
        if (double.IsNaN(_logBpm[reference])) return output;
        for (var i = 0; i < rows.Length; i++)
        {
            var d = Math.Abs(_logBpm[rows[i]] - _logBpm[reference]);
            d = Math.Min(d, Math.Abs(d - 1));
            var spread = 0.0;
            for (var k = 0; k < 3; k++) spread += Math.Abs(_featZ[rows[i], k] - _featZ[reference, k]);
            output[i] = -d / 0.08 - 0.5 * spread;
        }
        var known = output.Where(v => !double.IsNaN(v)).Order().ToArray();
        var fill = output.Any(double.IsFinite) ? Median(known) : 0.0;
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = double.IsNaN(output[i]) ? fill
                : double.IsPositiveInfinity(output[i]) ? double.MaxValue
                : double.IsNegativeInfinity(output[i]) ? -double.MaxValue : output[i];
        }
        return output;
    }

    private static double Median(double[] sorted) =>
        sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

    /// <summary>idf-weighted tag vector per vector row: sharing a rare tag (jazz, metal) counts much more than a common
    /// one (j-pop).</summary>
    private void TagVectors()
    {
        var n = _embIds.Count;
        _tagDims = _tagName.Length;
        var rowOfSong = new int[_songList.Count];
        Array.Fill(rowOfSong, -1);
        for (var row = 0; row < n; row++) rowOfSong[_songNumber[_embIds[row]]] = row;
        var counts = new int[_tagDims];
        for (var i = 0; i < _tagId.Length; i++)
        {
            if (rowOfSong[_tagSong[i]] >= 0) counts[_tagId[i]]++;
        }
        var weight = Enumerable.Range(0, _tagDims)
            .Select(t => (float)Math.Log((n + 1.0) / (counts[t] + 1.0)) * VibeGroupWeight.GetValueOrDefault(_tagGroup[t], 0.5f)).ToArray();
        _tagVectors = new float[n * _tagDims];
        for (var i = 0; i < _tagId.Length; i++)
        {
            var row = rowOfSong[_tagSong[i]];
            if (row >= 0) _tagVectors[row * _tagDims + _tagId[i]] = weight[_tagId[i]] * (0.5f + _tagScore[i]);
        }
        for (var row = 0; row < n; row++)
        {
            var slice = _tagVectors.AsSpan(row * _tagDims, _tagDims);
            var norm = 0f;
            foreach (var v in slice) norm += v * v;
            if (norm <= 0) continue;
            norm = MathF.Sqrt(norm);
            for (var k = 0; k < slice.Length; k++) slice[k] /= norm;
        }
    }

    private float TagDot(int a, int b)
    {
        var x = _tagVectors.AsSpan(a * _tagDims, _tagDims);
        var y = _tagVectors.AsSpan(b * _tagDims, _tagDims);
        var sum = 0f;
        for (var k = 0; k < _tagDims; k++) sum += x[k] * y[k];
        return sum;
    }

    /// <summary>Scores of songs outside the active sources / kinds pushed to the bottom.</summary>
    private static float[] OnlyActive(float[] scores, bool[] mask)
    {
        var output = new float[scores.Length];
        for (var i = 0; i < scores.Length; i++) output[i] = mask[i] ? scores[i] : float.NegativeInfinity;
        return output;
    }

    /// <summary>Indices of the k highest scores, highest first.</summary>
    internal static int[] TopK(float[] scores, int k)
    {
        k = Math.Min(k, scores.Length);
        var heap = new PriorityQueue<int, float>(k + 1);
        for (var i = 0; i < scores.Length; i++)
        {
            if (heap.Count < k) heap.Enqueue(i, scores[i]);
            else if (heap.TryPeek(out _, out var lowest) && scores[i] > lowest) heap.EnqueueDequeue(i, scores[i]);
        }
        var output = new int[heap.Count];
        for (var i = output.Length - 1; i >= 0; i--) output[i] = heap.Dequeue();
        return output;
    }

    // ---------------------------------------------------------------- similarity

    /// <summary>Song vector nudged towards its 3 nearest songs from other works (query expansion): smooths out what is
    /// specific to this one recording. +9% same artist / composer in the top 10 in the AMQ benchmark.</summary>
    private float[] Expanded(string id, int n = 3, float weight = 0.5f)
    {
        var row = _embIndex[id];
        var v = _emb.Get(row);
        var song = Songs[id];
        var near = new List<int>();
        foreach (var j in TopK(_emb.DotAll(v), 60))
        {
            var other = Songs[_embIds[j]];
            if (other.WorkId == song.WorkId || other.BaseTitle == song.BaseTitle) continue;
            near.Add(j);
            if (near.Count == n) break;
        }
        if (near.Count == 0) return v;
        var mean = _emb.Mean(near);
        for (var k = 0; k < v.Length; k++) v[k] += weight * mean[k];
        return v;
    }

    private float[] SeedSimilarity(string id)
    {
        var sims = _emb.DotAll(Expanded(id));
        for (var i = 0; i < sims.Length; i++) sims[i] /= 1.5f;
        return sims;
    }

    public List<Dictionary<string, object?>> Similar(string id, int n = 30)
    {
        if (!_embIndex.ContainsKey(id)) return [];
        var active = _active;
        var sims = SeedSimilarity(id);
        var order = TopK(OnlyActive(sims, active.Mask), n * 4 + 20);
        var song = Songs[id];
        var ids = Dedupe(order.Select(i => _embIds[i]), new HashSet<string> { song.SongKey }, new HashSet<string> { song.TitleKey }).Take(n);
        return ids.Select(i => SongView(i, new() { ["match"] = Math.Round(sims[_embIndex[i]], 3) })).ToList();
    }

    public List<Dictionary<string, object?>> Recommend(IEnumerable<string> likedIds, int n = 60, int? seed = null)
    {
        var liked = likedIds.Where(_embIndex.ContainsKey).ToList();
        if (liked.Count == 0) return [];
        var active = _active;
        var likedVectors = liked.Select(i => _emb.Get(_embIndex[i])).ToArray();
        var k = Math.Min(3, liked.Count);
        var score = new float[_emb.Count];
        var because = new int[_emb.Count];
        // close to several liked songs (mean of the best 3), not to their average
        Parallel.For(0, _emb.Count, row =>
        {
            Span<float> best = stackalloc float[3];
            best.Fill(float.NegativeInfinity);
            var top = float.NegativeInfinity;
            var sims = likedVectors.Length <= 1024 ? stackalloc float[likedVectors.Length] : new float[likedVectors.Length];
            _emb.Dots(row, likedVectors, sims);
            for (var j = 0; j < likedVectors.Length; j++)
            {
                var sim = sims[j];
                if (sim > top)
                {
                    top = sim;
                    because[row] = j;
                }
                for (var b = 0; b < k; b++)
                {
                    if (sim <= best[b]) continue;
                    for (var c = k - 1; c > b; c--) best[c] = best[c - 1];
                    best[b] = sim;
                    break;
                }
            }
            var sum = 0f;
            for (var b = 0; b < k; b++) sum += best[b];
            score[row] = sum / k;
        });
        if (seed is { } refresh)            // "refresh": a small shuffle in the ranking
        {
            var random = new Random(refresh);
            for (var i = 0; i < score.Length; i++) score[i] += (float)(Gaussian(random) * 0.012);
        }
        var order = TopK(OnlyActive(score, active.Mask), n * 8);
        var skipKeys = liked.Select(i => Songs[i].SongKey).ToHashSet();
        var skipTitles = liked.Select(i => Songs[i].TitleKey).ToHashSet();
        var output = new List<Dictionary<string, object?>>();
        var perArtist = new Dictionary<string, int>();
        var perWork = new Dictionary<string, int>();
        foreach (var id in Dedupe(order.Select(i => _embIds[i]), skipKeys, skipTitles))
        {
            var song = Songs[id];
            var artist = song.Artist.ToLowerInvariant();
            if (perArtist.GetValueOrDefault(artist) >= 2 || perWork.GetValueOrDefault(song.WorkId) >= 2) continue;
            perArtist[artist] = perArtist.GetValueOrDefault(artist) + 1;
            perWork[song.WorkId] = perWork.GetValueOrDefault(song.WorkId) + 1;
            var row = _embIndex[id];
            var source = Songs[liked[because[row]]];
            output.Add(SongView(id, new()
            {
                ["match"] = Math.Round(score[row], 3),
                ["because"] = new Dictionary<string, object?> { ["id"] = source.Id, ["name"] = source.Name },
            }));
            if (output.Count >= n) break;
        }
        return output;
    }

    /// <summary>
    /// The next discovery song.
    /// close   (default) every pick is measured against the song the run started from: the unplayed songs that sound
    ///         closest to it (MuQ + CLAP), share its genre / mood tags and have a similar tempo, loudness and punch.
    ///         The best few are played first, so the run walks outwards from the seed.
    /// explore follows the current song more, so the run drifts further from where it started.
    /// Never repeats a song already played (or another version / cover of it), avoids recent artists and works.
    /// Learns within the run: away from songs the listener skipped quickly, towards songs they liked.
    /// </summary>
    public Dictionary<string, object?>? RadioNext(string seed, string current, IReadOnlyList<string> history, string mode,
        IReadOnlyList<string> skipped, IReadOnlyList<string> liked)
    {
        if (!_embIndex.ContainsKey(seed)) seed = current;
        if (!_embIndex.ContainsKey(current)) current = seed;
        if (!_embIndex.ContainsKey(current)) return null;
        var active = _active;
        var si = _embIndex[seed];
        var ci = _embIndex[current];
        var seedSim = SeedSimilarity(seed);
        var explore = mode == "explore";
        int[] pool;
        double[] weights;          // seed sound, current sound, seed tags, current tags, seed features, current features
        int topK;
        double temperature;
        if (explore)
        {
            var near = _emb.DotAll(_emb.Get(ci));
            for (var i = 0; i < near.Length; i++) near[i] = 0.7f * near[i] + 0.3f * seedSim[i];
            pool = TopK(OnlyActive(near, active.Mask), 1500);
            weights = [0.15, 0.40, 0.08, 0.22, 0.0, 0.10];
            topK = 10;
            temperature = 0.3;
        }
        else
        {
            // the neighbourhood grows slowly with the run so long sessions don't run dry
            pool = TopK(OnlyActive(seedSim, active.Mask), 250 + 3 * history.Count);
            weights = [0.50, 0.10, 0.15, 0.05, 0.12, 0.03];
            topK = 5;
            temperature = 0.15;
        }

        var played = history.Where(Songs.ContainsKey).Select(i => Songs[i]).Append(Songs[current]).ToList();
        var skipKeys = played.Select(s => s.SongKey).ToHashSet();
        var skipTitles = played.Select(s => s.TitleKey).ToHashSet();
        var skipNames = played.Select(s => s.BaseTitle).ToHashSet();           // covers, acoustic / orchestra versions
        var recentArtists = new Dictionary<string, int>();                      // each recent play of an artist pushes it down
        foreach (var s in played.TakeLast(8)) recentArtists[s.Artist.ToLowerInvariant()] = recentArtists.GetValueOrDefault(s.Artist.ToLowerInvariant()) + 1;
        var recentWorks = played.TakeLast(4).Select(s => s.WorkId).ToHashSet();
        var keep = pool.Where(j =>
        {
            var s = Songs[_embIds[j]];
            return active.Mask[j] && !string.IsNullOrEmpty(s.File) && !skipKeys.Contains(s.SongKey) && !skipTitles.Contains(s.TitleKey)
                   && !skipNames.Contains(s.BaseTitle);
        }).ToArray();
        if (keep.Length == 0) return null;

        var currentVector = _emb.Get(ci);
        var parts = new[]
        {
            keep.Select(j => (double)seedSim[j]).ToArray(),
            keep.Select(j => (double)_emb.Dot(j, currentVector)).ToArray(),
            keep.Select(j => (double)TagDot(j, si)).ToArray(),
            keep.Select(j => (double)TagDot(j, ci)).ToArray(),
            FeatureSimilarity(keep, si),
            FeatureSimilarity(keep, ci),
        };
        var score = new double[keep.Length];
        for (var p = 0; p < parts.Length; p++)
        {
            if (weights[p] == 0) continue;
            var z = ZScores(parts[p]);
            for (var i = 0; i < score.Length; i++) score[i] += weights[p] * z[i];
        }
        // era: songs from around the same years tend to share production style
        if (Songs[seed].Year is { } seedYear and not 0)
        {
            var era = ZScores(keep.Select(j => Math.Exp(-Math.Abs((double)((Songs[_embIds[j]].Year is { } y and not 0 ? y : seedYear) - seedYear)) / 8)).ToArray());
            for (var i = 0; i < score.Length; i++) score[i] += (explore ? 0.04 : 0.08) * era[i];
        }
        var skippedRows = skipped.Where(_embIndex.ContainsKey).Select(i => _embIndex[i]).TakeLast(12).ToArray();
        if (skippedRows.Length > 0)
        {
            var z = ZScores(keep.Select(j => skippedRows.Max(s => (double)_emb.Dot(j, s))).ToArray());
            for (var i = 0; i < score.Length; i++) score[i] -= 0.35 * z[i];
        }
        var likedRows = liked.Where(_embIndex.ContainsKey).Select(i => _embIndex[i]).TakeLast(12).ToArray();
        if (likedRows.Length > 0)
        {
            var z = ZScores(keep.Select(j => likedRows.Max(s => (double)_emb.Dot(j, s))).ToArray());
            for (var i = 0; i < score.Length; i++) score[i] += 0.25 * z[i];
        }
        for (var i = 0; i < keep.Length; i++)
        {
            var s = Songs[_embIds[keep[i]]];
            score[i] -= 0.45 * recentArtists.GetValueOrDefault(s.Artist.ToLowerInvariant()) + (recentWorks.Contains(s.WorkId) ? 0.3 : 0);
        }

        var top = TopK(score.Select(v => (float)v).ToArray(), topK);
        var probabilities = top.Select(t => Math.Exp((score[t] - score[top[0]]) / temperature)).ToArray();
        var roll = Random.Shared.NextDouble() * probabilities.Sum();
        var pick = top[^1];
        for (var t = 0; t < top.Length; t++)
        {
            roll -= probabilities[t];
            if (roll > 0) continue;
            pick = top[t];
            break;
        }
        var pickId = _embIds[keep[pick]];
        return SongView(pickId, new() { ["match"] = Math.Round(seedSim[_embIndex[pickId]], 3) });
    }

    private static double[] ZScores(double[] x)
    {
        var mean = x.Average();
        var std = Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / x.Length);
        return x.Select(v => (v - mean) / (std + 1e-6)).ToArray();
    }

    private static double Gaussian(Random random)
    {
        var u = 1.0 - random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * random.NextDouble());
    }
}
