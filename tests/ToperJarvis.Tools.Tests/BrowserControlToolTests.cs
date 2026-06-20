using ToperJarvis.Tools.Web;

namespace ToperJarvis.Tools.Tests;

public class BrowserControlToolTests
{
    [Theory]
    [InlineData("instagram", "https://instagram.com")]          // goła nazwa → .com
    [InlineData("instagram.com", "https://instagram.com")]      // domena → https
    [InlineData("https://example.com/x", "https://example.com/x")] // pełny URL bez zmian
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("", "about:blank")]                              // pusty → about:blank
    public void NormalizeUrl_mapuje_poprawnie(string input, string expected)
    {
        Assert.Equal(expected, BrowserControlTool.NormalizeUrl(input));
    }

    [Theory]
    [InlineData("koty psy", "google", "https://www.google.com/search?q=koty%20psy")]
    [InlineData("test", "bing", "https://www.bing.com/search?q=test")]
    [InlineData("test", "duckduckgo", "https://duckduckgo.com/?q=test")]
    [InlineData("test", "nieznana", "https://www.google.com/search?q=test")] // fallback Google
    public void SearchUrl_buduje_adres(string query, string engine, string expected)
    {
        Assert.Equal(expected, BrowserControlTool.SearchUrl(query, engine));
    }

    [Fact]
    public void DefaultProfileDir_w_LocalAppData()
    {
        var dir = BrowserSession.DefaultProfileDir();

        Assert.Contains("ToperJarvis", dir);
        Assert.EndsWith("browser", dir);
    }
}
