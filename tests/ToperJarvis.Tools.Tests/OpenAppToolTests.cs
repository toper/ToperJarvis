using ToperJarvis.Tools.System;

namespace ToperJarvis.Tools.Tests;

public class OpenAppToolTests
{
    [Theory]
    [InlineData("chrome", "chrome")]                 // alias aplikacji
    [InlineData("kalkulator", "calc")]               // alias PL
    [InlineData("youtube", "https://www.youtube.com")] // web-app
    [InlineData("https://example.com", "https://example.com")] // pełny URL
    [InlineData("example.com", "https://example.com")]         // domena → URL
    [InlineData("notepad.exe", "notepad.exe")]       // plik wykonywalny → NIE URL
    [InlineData("nieznana_apka", "nieznana_apka")]   // fallback do powłoki
    public void Resolve_mapuje_poprawnie(string input, string expected)
    {
        Assert.Equal(expected, OpenAppTool.Resolve(input));
    }
}
