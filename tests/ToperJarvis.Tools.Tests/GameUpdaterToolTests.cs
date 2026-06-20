using ToperJarvis.Tools.System;

namespace ToperJarvis.Tools.Tests;

public class GameUpdaterToolTests
{
    [Fact]
    public void ResolveAppId_jawny_appid()
    {
        Assert.Equal("12345", GameUpdaterTool.ResolveAppId(null, "12345"));
    }

    [Theory]
    [InlineData("cs2", "730")]
    [InlineData("Dota 2", "570")]
    [InlineData("cyberpunk", "1091500")]
    public void ResolveAppId_po_nazwie(string name, string expected)
    {
        Assert.Equal(expected, GameUpdaterTool.ResolveAppId(name, null));
    }

    [Fact]
    public void ResolveAppId_appid_ma_pierwszenstwo()
    {
        Assert.Equal("999", GameUpdaterTool.ResolveAppId("cs2", "999"));
    }

    [Theory]
    [InlineData("nieznana gra", null)]
    [InlineData(null, "abc")]   // niepoprawny appid (nie-cyfry) i brak nazwy
    [InlineData(null, null)]
    public void ResolveAppId_nieznane_null(string? name, string? appId)
    {
        Assert.Null(GameUpdaterTool.ResolveAppId(name, appId));
    }
}
