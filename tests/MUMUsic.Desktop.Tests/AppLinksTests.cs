namespace MUMUsic.Desktop.Tests;

public class AppLinksTests
{
    [Theory]
    [InlineData("mumusic://play/a123", "a123")]
    [InlineData("mumusic://play/e456/", "e456")]
    [InlineData("MUMUSIC://play/A7", "a7")]
    [InlineData("mumusic://play/x1", null)]
    [InlineData("mumusic://play/a1/../../evil", null)]
    [InlineData("https://example.com", null)]
    [InlineData(null, null)]
    public void ReadsTheSongOfALink(string? link, string? song) => Assert.Equal(song, AppLinks.SongId(link));

    [Fact]
    public void FindsTheLinkAmongArguments() =>
        Assert.Equal("mumusic://play/a1", AppLinks.FromArgs(["--debug", "mumusic://play/a1"]));
}
