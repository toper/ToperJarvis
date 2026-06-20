using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Core.Memory;

namespace ToperJarvis.Core.Tests.Memory;

public class JsonMemoryStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "toperjarvis_test_" + Guid.NewGuid().ToString("N"), "mem.json");

    private JsonMemoryStore NewStore() => new(_path, NullLogger<JsonMemoryStore>.Instance);

    [Fact]
    public void Remember_zapisuje_i_pojawia_sie_w_prompcie()
    {
        var store = NewStore();
        store.Remember("imie", "Jarek", "identity");

        var prompt = store.FormatForPrompt();
        Assert.Contains("imie", prompt);
        Assert.Contains("Jarek", prompt);
    }

    [Fact]
    public void Remember_trwa_miedzy_instancjami()
    {
        NewStore().Remember("ulubiony_kolor", "niebieski", "preferences");

        // nowa instancja czyta z tego samego pliku
        var prompt = NewStore().FormatForPrompt();
        Assert.Contains("niebieski", prompt);
    }

    [Fact]
    public void Forget_usuwa_fakt()
    {
        var store = NewStore();
        store.Remember("projekt", "ToperJarvis", "projects");
        store.Forget("projekt", "projects");

        Assert.DoesNotContain("ToperJarvis", store.FormatForPrompt());
    }

    [Fact]
    public void Nieznana_kategoria_trafia_do_notes()
    {
        var store = NewStore();
        var result = store.Remember("cos", "wartosc", "nieistniejaca");

        Assert.Contains("notes/cos", result);
    }

    [Fact]
    public void Dluga_wartosc_jest_przycinana()
    {
        var store = NewStore();
        var longValue = new string('x', 500);
        var result = store.Remember("dlugi", longValue, "notes");

        Assert.EndsWith("…", result);
        Assert.True(result.Length < 500 + 50);
    }

    [Fact]
    public void Pusty_klucz_lub_wartosc_jest_pomijany()
    {
        var store = NewStore();
        Assert.Contains("Pominięto", store.Remember("", "x"));
        Assert.Contains("Pominięto", store.Remember("k", ""));
    }

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path);
        if (dir is not null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
