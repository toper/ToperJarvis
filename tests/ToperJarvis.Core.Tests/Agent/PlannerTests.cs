using ToperJarvis.Core.Agent;

namespace ToperJarvis.Core.Tests.Agent;

public class PlannerTests
{
    [Fact]
    public void ParsePlan_tablica_stringow()
    {
        var steps = Planner.ParsePlan("[\"otwórz przeglądarkę\", \"wyszukaj pogodę\"]");
        Assert.Equal(2, steps.Count);
        Assert.Equal("otwórz przeglądarkę", steps[0]);
    }

    [Fact]
    public void ParsePlan_usuwa_ogrodzenie_kodu()
    {
        var steps = Planner.ParsePlan("```json\n[\"krok a\", \"krok b\"]\n```");
        Assert.Equal(new[] { "krok a", "krok b" }, steps);
    }

    [Fact]
    public void ParsePlan_tablica_obiektow_z_polem_opisu()
    {
        var steps = Planner.ParsePlan("[{\"step\":\"pierwszy\"},{\"description\":\"drugi\"}]");
        Assert.Equal(new[] { "pierwszy", "drugi" }, steps);
    }

    [Fact]
    public void ParsePlan_ogranicza_do_pieciu_krokow()
    {
        var steps = Planner.ParsePlan("[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\"]");
        Assert.Equal(5, steps.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("brak tablicy")]
    [InlineData("{nie json")]
    public void ParsePlan_niepoprawne_wejscie_zwraca_puste(string raw)
    {
        Assert.Empty(Planner.ParsePlan(raw));
    }
}
