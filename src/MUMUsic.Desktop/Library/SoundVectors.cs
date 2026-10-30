using System.Numerics;

namespace MUMUsic.Desktop.Library;

/// <summary>
/// Unit sound vectors (whitened MuQ + CLAP), kept as int8 rows like in the file (1.4 kB per song) with one float scale
/// per row (1 / norm). Rows are widened to float a few at a time while computing dot products, so the whole matrix is
/// never copied as floats.
/// </summary>
internal sealed class SoundVectors
{
    private readonly sbyte[] _values;
    private readonly float[] _scale;

    public SoundVectors(sbyte[] values, int count, int dims)
    {
        _values = values;
        Count = count;
        Dims = dims;
        _scale = new float[count];
        Parallel.For(0, count, row =>
        {
            var squares = 0.0;
            foreach (var value in Row(row)) squares += value * value;
            _scale[row] = squares > 0 ? (float)(1 / Math.Sqrt(squares)) : 0;
        });
    }

    public int Count { get; }
    public int Dims { get; }

    private ReadOnlySpan<sbyte> Row(int row) => _values.AsSpan(row * Dims, Dims);

    /// <summary>The float unit vector of one row.</summary>
    public float[] Get(int row)
    {
        var output = new float[Dims];
        var values = Row(row);
        var scale = _scale[row];
        for (var k = 0; k < Dims; k++) output[k] = values[k] * scale;
        return output;
    }

    /// <summary>Mean of the unit vectors of some rows.</summary>
    public float[] Mean(IReadOnlyList<int> rows)
    {
        var output = new float[Dims];
        foreach (var row in rows)
        {
            var values = Row(row);
            var scale = _scale[row];
            for (var k = 0; k < Dims; k++) output[k] += values[k] * scale;
        }
        for (var k = 0; k < Dims; k++) output[k] /= rows.Count;
        return output;
    }

    /// <summary>Unit row · v.</summary>
    public float Dot(int row, ReadOnlySpan<float> v) => DotInt8(Row(row), v) * _scale[row];

    /// <summary>Unit row · each of <paramref name="vectors"/>: the row is widened to float once for all of them.</summary>
    public void Dots(int row, float[][] vectors, Span<float> output)
    {
        Span<float> widened = Dims <= 4096 ? stackalloc float[Dims] : new float[Dims];
        var values = Row(row);
        var scale = _scale[row];
        for (var k = 0; k < Dims; k++) widened[k] = values[k] * scale;
        for (var j = 0; j < vectors.Length; j++) output[j] = DotFloat(widened, vectors[j]);
    }

    private static float DotFloat(ReadOnlySpan<float> x, ReadOnlySpan<float> y)
    {
        var width = Vector<float>.Count;
        var sum = Vector<float>.Zero;
        var k = 0;
        for (; k <= x.Length - width; k += width) sum += new Vector<float>(x.Slice(k, width)) * new Vector<float>(y.Slice(k, width));
        var total = Vector.Sum(sum);
        for (; k < x.Length; k++) total += x[k] * y[k];
        return total;
    }

    /// <summary>Unit row a · unit row b.</summary>
    public float Dot(int a, int b)
    {
        var x = Row(a);
        var y = Row(b);
        var sum = 0;
        for (var k = 0; k < Dims; k++) sum += x[k] * y[k];
        return sum * _scale[a] * _scale[b];
    }

    /// <summary>Every row · v.</summary>
    public float[] DotAll(float[] v)
    {
        var output = new float[Count];
        Parallel.For(0, (Count + 1023) / 1024, block =>
        {
            var end = Math.Min(Count, (block + 1) * 1024);
            for (var row = block * 1024; row < end; row++) output[row] = Dot(row, v);
        });
        return output;
    }

    private static float DotInt8(ReadOnlySpan<sbyte> values, ReadOnlySpan<float> v)
    {
        var step = Vector<sbyte>.Count;
        var width = Vector<float>.Count;
        var sum = Vector<float>.Zero;
        var k = 0;
        for (; k <= values.Length - step; k += step)
        {
            Vector.Widen(new Vector<sbyte>(values.Slice(k, step)), out var low, out var high);
            Vector.Widen(low, out var i0, out var i1);
            Vector.Widen(high, out var i2, out var i3);
            sum += Vector.ConvertToSingle(i0) * new Vector<float>(v.Slice(k, width));
            sum += Vector.ConvertToSingle(i1) * new Vector<float>(v.Slice(k + width, width));
            sum += Vector.ConvertToSingle(i2) * new Vector<float>(v.Slice(k + 2 * width, width));
            sum += Vector.ConvertToSingle(i3) * new Vector<float>(v.Slice(k + 3 * width, width));
        }
        var total = Vector.Sum(sum);
        for (; k < values.Length; k++) total += values[k] * v[k];
        return total;
    }
}
