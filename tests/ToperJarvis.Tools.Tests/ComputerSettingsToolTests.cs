using ToperJarvis.Tools.System;

namespace ToperJarvis.Tools.Tests;

public class ComputerSettingsToolTests
{
    [Theory]
    [InlineData("volume_up", 0xAF)]
    [InlineData("głośniej", 0xAF)]
    [InlineData("volume_down", 0xAE)]
    [InlineData("ciszej", 0xAE)]
    [InlineData("mute", 0xAD)]
    [InlineData("wycisz", 0xAD)]
    public void ResolveKey_mapuje_akcje(string action, int expectedVk)
    {
        Assert.Equal((byte)expectedVk, ComputerSettingsTool.ResolveKey(action));
    }

    [Theory]
    [InlineData("nieznana")]
    [InlineData("")]
    public void ResolveKey_nieznana_akcja_null(string action)
    {
        Assert.Null(ComputerSettingsTool.ResolveKey(action));
    }
}
