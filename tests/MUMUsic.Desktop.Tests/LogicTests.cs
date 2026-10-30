using MUMUsic.Desktop.Library;
using MUMUsic.Desktop.Updates;

namespace MUMUsic.Desktop.Tests;

public class UpdateVersionTests
{
    [Theory]
    [InlineData("2.0.0", "1.1.1", 1)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    [InlineData("2.0", "2.0.0", -1)]
    [InlineData("1.1.1", "2.0.0", -1)]
    [InlineData(null, "0.0.1", -1)]
    public void ComparesVersionsByNumber(string? a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(UpdateService.CompareVersions(a, b)));
}

public class LibraryLogicTests
{
    [Theory]
    [InlineData("unravel (Acoustic Version)", "unravel")]
    [InlineData("Connect -Orchestra ver.-", "connect")]
    [InlineData("Hikaru Nara ～TV size～", "hikaru nara")]
    [InlineData("(only brackets)", "(only brackets)")]
    public void BaseTitleDropsVersionMarks(string name, string expected) => Assert.Equal(expected, MusicLibrary.BaseTitle(name));

    [Fact]
    public void TopKReturnsHighestFirstAndKeepsMaskedLast()
    {
        float[] scores = [0.1f, 0.9f, float.NegativeInfinity, 0.5f, 0.7f];
        Assert.Equal([1, 4, 3], MusicLibrary.TopK(scores, 3));
        Assert.Equal([1, 4, 3, 0, 2], MusicLibrary.TopK(scores, 10));
    }

    [Fact]
    public void QuotePathKeepsSlashes() =>
        Assert.Equal("userup/weba/a%20b%23c.weba", MusicLibrary.QuotePath("userup/weba/a b#c.weba"));
}
