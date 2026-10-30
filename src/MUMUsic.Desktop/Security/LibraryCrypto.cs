using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MUMUsic.Desktop.Security;

/// <summary>A wrong access key or a bad library file, with a message for the user.</summary>
internal sealed class WrongKeyException(string message) : Exception(message);

/// <summary>
/// Reads encrypted library files (.mumu), written by tools/publish.py. Integers are big-endian:
/// <code>
/// "MUMU" | format (1 byte) | salt (16) | key check (34) | nonce prefix (8) | chunk | chunk | ...
/// chunk = length (4) | AES-256-GCM(zlib(next 4 MB of the plain file))
/// </code>
/// The key is scrypt(access key, salt); the key check tells a wrong key from a damaged file. Each chunk's nonce is the
/// prefix + the chunk number, and the last chunk is authenticated with the associated data "end", so a reordered or
/// truncated file fails. This only keeps the library unreadable to whoever finds the file: every user shares the key.
/// </summary>
internal static class LibraryCrypto
{
    private static readonly byte[] Magic = "MUMU"u8.ToArray();
    private const byte Format = 1;
    private static readonly byte[] CheckText = Encoding.ASCII.GetBytes("MUMUsic access key");
    private const int TagSize = 16;
    private static readonly int CheckSize = CheckText.Length + TagSize;
    private static readonly int HeaderSize = Magic.Length + 1 + 16 + CheckSize + 8;

    public static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Scrypt.DeriveKey(Encoding.UTF8.GetBytes(passphrase), salt, 1 << 15, 8, 1, 32);

    /// <summary>Tells whether a key is right without decrypting the library (in the file header and, base64, in the
    /// manifest). A fixed nonce is fine: it is the only message encrypted with nonce 0 under this key.</summary>
    public static byte[] CheckBytes(byte[] key)
    {
        using var aes = new AesGcm(key, TagSize);
        var output = new byte[CheckSize];
        aes.Encrypt(new byte[12], CheckText, output.AsSpan(0, CheckText.Length), output.AsSpan(CheckText.Length));
        return output;
    }

    public static bool IsRightKey(string passphrase, string saltBase64, string check)
    {
        try
        {
            var key = DeriveKey(passphrase, Convert.FromBase64String(saltBase64));
            return Convert.ToBase64String(CheckBytes(key)) == check;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Writes the plain file to <paramref name="destination"/> through a temporary file.</summary>
    public static void DecryptFile(string source, string destination, string passphrase, Action<double>? progress = null)
    {
        var temp = destination + ".tmp";
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = File.Create(temp))
            {
                Decrypt(input, output, passphrase, progress);
            }
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private static void Decrypt(FileStream input, Stream output, string passphrase, Action<double>? progress)
    {
        var size = input.Length;
        var head = new byte[HeaderSize];
        if (input.ReadAtLeast(head, HeaderSize, throwOnEndOfStream: false) < HeaderSize
            || !head.AsSpan(0, 4).SequenceEqual(Magic) || head[4] != Format)
        {
            throw new WrongKeyException("This is not a MUMUsic library file");
        }
        var salt = head[5..21];
        var check = head[21..(21 + CheckSize)];
        var prefix = head[(21 + CheckSize)..];
        var key = DeriveKey(passphrase, salt);
        if (!CheckBytes(key).AsSpan().SequenceEqual(check)) throw new WrongKeyException("Wrong access key");

        using var aes = new AesGcm(key, TagSize);
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        var lengthBytes = new byte[4];
        for (uint index = 0; ; index++)
        {
            if (input.ReadAtLeast(lengthBytes, 4, throwOnEndOfStream: false) < 4)
            {
                throw new WrongKeyException("The library file is incomplete");
            }
            var sealedSize = (int)BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
            if (sealedSize < TagSize) throw new WrongKeyException("The library file is damaged");
            var sealedChunk = new byte[sealedSize];
            if (input.ReadAtLeast(sealedChunk, sealedSize, throwOnEndOfStream: false) < sealedSize)
            {
                throw new WrongKeyException("The library file is incomplete");
            }
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), index);
            var isLast = input.Position >= size;
            var plain = new byte[sealedSize - TagSize];
            try
            {
                aes.Decrypt(nonce, sealedChunk.AsSpan(0, plain.Length), sealedChunk.AsSpan(plain.Length), plain,
                    isLast ? "end"u8 : ReadOnlySpan<byte>.Empty);
            }
            catch (AuthenticationTagMismatchException)
            {
                throw new WrongKeyException("The library file is damaged");
            }
            using (var zlib = new ZLibStream(new MemoryStream(plain), CompressionMode.Decompress))
            {
                zlib.CopyTo(output);
            }
            progress?.Invoke((double)input.Position / size);
            if (isLast) return;
        }
    }
}
