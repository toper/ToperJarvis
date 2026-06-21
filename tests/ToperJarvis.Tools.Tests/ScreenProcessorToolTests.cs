using ToperJarvis.Tools.Vision;

namespace ToperJarvis.Tools.Tests;

public class ScreenProcessorToolTests
{
    [Fact]
    public void ResolvePrompt_pusty_daje_domyslny_opis()
    {
        Assert.Equal("Opisz zwięźle po polsku, co widać na ekranie.", ScreenProcessorTool.ResolvePrompt(null));
        Assert.Equal("Opisz zwięźle po polsku, co widać na ekranie.", ScreenProcessorTool.ResolvePrompt("   "));
    }

    [Theory]
    [InlineData("co tu pisze?", "co tu pisze?")]
    [InlineData("  jaki błąd widać  ", "jaki błąd widać")] // trymowanie
    public void ResolvePrompt_zwraca_pytanie_uzytkownika(string question, string expected)
    {
        Assert.Equal(expected, ScreenProcessorTool.ResolvePrompt(question));
    }
}
