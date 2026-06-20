using ToperJarvis.Tools.Web;

namespace ToperJarvis.Tools.Tests;

public class YouTubeVideoToolTests
{
    [Fact]
    public void BuildTarget_trending()
    {
        Assert.Equal("https://www.youtube.com/feed/trending",
            YouTubeVideoTool.BuildTarget("trending", null, null));
    }

    [Fact]
    public void BuildTarget_wyszukiwanie_po_frazie()
    {
        var target = YouTubeVideoTool.BuildTarget("search", "lo-fi beats", null);
        Assert.StartsWith("https://www.youtube.com/results?search_query=", target);
        Assert.Contains("lo-fi", target.Replace("%2D", "-")); // zakodowane, ale fraza obecna
    }

    [Fact]
    public void BuildTarget_poprawny_url_youtube()
    {
        var target = YouTubeVideoTool.BuildTarget("play", null, "https://youtu.be/abc123");
        Assert.Contains("youtu.be/abc123", target);
    }

    [Fact]
    public void BuildTarget_obcy_url_spada_do_wyszukiwania_lub_home()
    {
        // URL spoza YouTube jest ignorowany — brak frazy => strona główna
        Assert.Equal("https://www.youtube.com", YouTubeVideoTool.BuildTarget("play", null, "https://evil.example.com/x"));
    }

    [Fact]
    public void BuildTarget_brak_parametrow_home()
    {
        Assert.Equal("https://www.youtube.com", YouTubeVideoTool.BuildTarget("search", null, null));
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=x", true)]
    [InlineData("youtube.com/watch?v=x", true)]
    [InlineData("https://youtu.be/x", true)]
    [InlineData("https://evil.example.com", false)]
    [InlineData("https://notyoutube.com", false)]
    public void IsYouTubeUrl_waliduje_host(string url, bool expected)
    {
        Assert.Equal(expected, YouTubeVideoTool.IsYouTubeUrl(url, out _));
    }
}
