using ToperJarvis.Llm;

namespace ToperJarvis.Core.Tests.Llm;

public class SentenceAccumulatorTests
{
    [Fact]
    public void Wydziela_zdania_po_granicy_interpunkcyjnej()
    {
        var acc = new SentenceAccumulator();

        var first = acc.Add("Cześć. ").ToList();
        Assert.Equal(new[] { "Cześć." }, first);

        // niedomknięte zdanie nie jest jeszcze zwracane
        Assert.Empty(acc.Add("Jak się "));

        var second = acc.Add("masz? ").ToList();
        Assert.Equal(new[] { "Jak się masz?" }, second);
    }

    [Fact]
    public void Nie_tnie_liczb_dziesietnych()
    {
        var acc = new SentenceAccumulator();

        Assert.Empty(acc.Add("Wynik to 3.5"));
        var done = acc.Add(" stopnia. ").ToList();

        Assert.Equal(new[] { "Wynik to 3.5 stopnia." }, done);
    }

    [Fact]
    public void Flush_zwraca_niedomknieta_reszte()
    {
        var acc = new SentenceAccumulator();

        Assert.Empty(acc.Add("Bez kropki na koncu"));
        Assert.Equal("Bez kropki na koncu", acc.Flush());
        Assert.Null(acc.Flush());
    }

    [Fact]
    public void Dziali_zdania_rozdzielone_pusta_linia()
    {
        var acc = new SentenceAccumulator();

        var result = acc.Add("Pierwszy akapit\n\nDrugi").ToList();
        Assert.Equal(new[] { "Pierwszy akapit" }, result);
    }
}
