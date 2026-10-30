using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace MUMUsic.Desktop.Security;

/// <summary>scrypt (RFC 7914), which .NET doesn't ship. The library key is derived with it (tools/publish.py uses
/// Python's hashlib.scrypt), so this must give the same bytes; the tests check it against a known vector.</summary>
internal static class Scrypt
{
    public static byte[] DeriveKey(byte[] password, byte[] salt, int n, int r, int p, int length)
    {
        if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("N must be a power of two", nameof(n));
        var blockSize = 128 * r;
        var b = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, p * blockSize);
        var x = new uint[32 * r];
        var y = new uint[32 * r];
        var v = new uint[32 * r * n];
        for (var i = 0; i < p; i++) RoMix(b.AsSpan(i * blockSize, blockSize), r, n, x, y, v);
        return Rfc2898DeriveBytes.Pbkdf2(password, b, 1, HashAlgorithmName.SHA256, length);
    }

    private static void RoMix(Span<byte> block, int r, int n, uint[] x, uint[] y, uint[] v)
    {
        var words = 32 * r;
        for (var k = 0; k < words; k++) x[k] = BinaryPrimitives.ReadUInt32LittleEndian(block[(k * 4)..]);
        for (var i = 0; i < n; i++)
        {
            Array.Copy(x, 0, v, i * words, words);
            BlockMix(x, y, r);
        }
        for (var i = 0; i < n; i++)
        {
            var j = (int)(x[(2 * r - 1) * 16] & (uint)(n - 1));
            for (var k = 0; k < words; k++) x[k] ^= v[j * words + k];
            BlockMix(x, y, r);
        }
        for (var k = 0; k < words; k++) BinaryPrimitives.WriteUInt32LittleEndian(block[(k * 4)..], x[k]);
    }

    // B -> B': Salsa20/8 over each 64-byte block, even blocks first then odd ones.
    private static void BlockMix(uint[] b, uint[] y, int r)
    {
        Span<uint> t = stackalloc uint[16];
        b.AsSpan((2 * r - 1) * 16, 16).CopyTo(t);
        for (var i = 0; i < 2 * r; i++)
        {
            for (var k = 0; k < 16; k++) t[k] ^= b[i * 16 + k];
            Salsa208(t);
            var target = (i % 2 == 0 ? i / 2 : r + i / 2) * 16;
            t.CopyTo(y.AsSpan(target, 16));
        }
        Array.Copy(y, b, 32 * r);
    }

    private static void Salsa208(Span<uint> b)
    {
        Span<uint> x = stackalloc uint[16];
        b.CopyTo(x);
        for (var i = 0; i < 8; i += 2)
        {
            x[4] ^= BitOperations.RotateLeft(x[0] + x[12], 7); x[8] ^= BitOperations.RotateLeft(x[4] + x[0], 9);
            x[12] ^= BitOperations.RotateLeft(x[8] + x[4], 13); x[0] ^= BitOperations.RotateLeft(x[12] + x[8], 18);
            x[9] ^= BitOperations.RotateLeft(x[5] + x[1], 7); x[13] ^= BitOperations.RotateLeft(x[9] + x[5], 9);
            x[1] ^= BitOperations.RotateLeft(x[13] + x[9], 13); x[5] ^= BitOperations.RotateLeft(x[1] + x[13], 18);
            x[14] ^= BitOperations.RotateLeft(x[10] + x[6], 7); x[2] ^= BitOperations.RotateLeft(x[14] + x[10], 9);
            x[6] ^= BitOperations.RotateLeft(x[2] + x[14], 13); x[10] ^= BitOperations.RotateLeft(x[6] + x[2], 18);
            x[3] ^= BitOperations.RotateLeft(x[15] + x[11], 7); x[7] ^= BitOperations.RotateLeft(x[3] + x[15], 9);
            x[11] ^= BitOperations.RotateLeft(x[7] + x[3], 13); x[15] ^= BitOperations.RotateLeft(x[11] + x[7], 18);
            x[1] ^= BitOperations.RotateLeft(x[0] + x[3], 7); x[2] ^= BitOperations.RotateLeft(x[1] + x[0], 9);
            x[3] ^= BitOperations.RotateLeft(x[2] + x[1], 13); x[0] ^= BitOperations.RotateLeft(x[3] + x[2], 18);
            x[6] ^= BitOperations.RotateLeft(x[5] + x[4], 7); x[7] ^= BitOperations.RotateLeft(x[6] + x[5], 9);
            x[4] ^= BitOperations.RotateLeft(x[7] + x[6], 13); x[5] ^= BitOperations.RotateLeft(x[4] + x[7], 18);
            x[11] ^= BitOperations.RotateLeft(x[10] + x[9], 7); x[8] ^= BitOperations.RotateLeft(x[11] + x[10], 9);
            x[9] ^= BitOperations.RotateLeft(x[8] + x[11], 13); x[10] ^= BitOperations.RotateLeft(x[9] + x[8], 18);
            x[12] ^= BitOperations.RotateLeft(x[15] + x[14], 7); x[13] ^= BitOperations.RotateLeft(x[12] + x[15], 9);
            x[14] ^= BitOperations.RotateLeft(x[13] + x[12], 13); x[15] ^= BitOperations.RotateLeft(x[14] + x[13], 18);
        }
        for (var i = 0; i < 16; i++) b[i] += x[i];
    }
}
