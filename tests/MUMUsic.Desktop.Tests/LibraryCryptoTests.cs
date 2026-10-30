using System.Security.Cryptography;
using System.Text;
using MUMUsic.Desktop.Security;

namespace MUMUsic.Desktop.Tests;

public class LibraryCryptoTests
{
    private const string FixtureKey = "test key é";
    private const string FixtureSalt = "4V8cZsu4wGEtAcQ2f+ylqQ==";
    private const string FixtureCheck = "/Ta/fhbad9ZDh/NjpoTPHRjhoKCrM+OsIc13zIGuBbxtoQ==";
    private const string FixturePlainSha256 = "f14a9cfc6a6b68c99104551bce71b1abc8571ffcaa394cbe877200d1330a8855";

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "library-fixture.bin");

    [Fact]
    public void ScryptMatchesRfc7914()
    {
        var key = Scrypt.DeriveKey(Encoding.ASCII.GetBytes("password"), Encoding.ASCII.GetBytes("NaCl"), 1024, 8, 16, 64);
        Assert.Equal("fdbabe1c9d3472007856e7190d01e9fe7c6ad7cbc8237830e77376634b3731622eaf30d92e22a3886ff109279d9830dac727afb94a83ee6d8360cbdfa2cc0640",
            Convert.ToHexString(key).ToLowerInvariant());
    }

    [Fact]
    public void KeyCheckMatchesTheManifest()
    {
        Assert.True(LibraryCrypto.IsRightKey(FixtureKey, FixtureSalt, FixtureCheck));
        Assert.False(LibraryCrypto.IsRightKey("wrong key", FixtureSalt, FixtureCheck));
        Assert.False(LibraryCrypto.IsRightKey(FixtureKey, "not base64!", FixtureCheck));
    }

    [Fact]
    public void DecryptsWhatToolsPublishWrites()
    {
        var output = Path.Combine(Path.GetTempPath(), $"mumu-test-{Guid.NewGuid():N}.db");
        try
        {
            var steps = new List<double>();
            LibraryCrypto.DecryptFile(Fixture, output, FixtureKey, steps.Add);
            Assert.Equal(FixturePlainSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant());
            Assert.Equal(2, steps.Count);           // 5.5 MB: two 4 MB chunks
            Assert.Equal(1.0, steps[^1]);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void WrongKeyAndDamageAreToldApart()
    {
        var output = Path.Combine(Path.GetTempPath(), $"mumu-test-{Guid.NewGuid():N}.db");
        var damaged = Path.Combine(Path.GetTempPath(), $"mumu-test-{Guid.NewGuid():N}.mumu");
        try
        {
            Assert.Equal("Wrong access key", Assert.Throws<WrongKeyException>(() => LibraryCrypto.DecryptFile(Fixture, output, "nope")).Message);
            var bytes = File.ReadAllBytes(Fixture);
            bytes[^5] ^= 0xFF;
            File.WriteAllBytes(damaged, bytes);
            Assert.Equal("The library file is damaged", Assert.Throws<WrongKeyException>(() => LibraryCrypto.DecryptFile(damaged, output, FixtureKey)).Message);
            File.WriteAllBytes(damaged, File.ReadAllBytes(Fixture)[..^100]);
            Assert.Throws<WrongKeyException>(() => LibraryCrypto.DecryptFile(damaged, output, FixtureKey));
            Assert.False(File.Exists(output));
        }
        finally
        {
            File.Delete(output);
            File.Delete(damaged);
        }
    }

    [Fact]
    public void ProtectedKeyRoundTrips()
    {
        Assert.Equal(FixtureKey, KeyProtection.Unprotect(KeyProtection.Protect(FixtureKey)));
        Assert.Null(KeyProtection.Unprotect("not base64!"));
        Assert.Null(KeyProtection.Unprotect(null));
    }
}
