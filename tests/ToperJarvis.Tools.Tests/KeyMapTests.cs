using ToperJarvis.Tools.Input;

namespace ToperJarvis.Tools.Tests;

public class KeyMapTests
{
    [Theory]
    [InlineData("enter", 0x0D)]
    [InlineData("esc", 0x1B)]
    [InlineData("a", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("5", 0x35)]
    [InlineData("f1", 0x70)]
    [InlineData("f12", 0x7B)]
    [InlineData("ctrl", 0x11)]
    public void ParseKey_rozpoznaje_klawisze(string key, int expected)
    {
        Assert.Equal((byte)expected, KeyMap.ParseKey(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nieznany")]
    [InlineData("f13")]
    public void ParseKey_nieznany_null(string key)
    {
        Assert.Null(KeyMap.ParseKey(key));
    }

    [Fact]
    public void ParseHotkey_rozklada_skrot()
    {
        var keys = KeyMap.ParseHotkey("ctrl+shift+s");
        Assert.Equal(new byte[] { 0x11, 0x10, 0x53 }, keys);
    }

    [Fact]
    public void ParseHotkey_nieznany_skladnik_zwraca_puste()
    {
        Assert.Empty(KeyMap.ParseHotkey("ctrl+zzz"));
    }
}
