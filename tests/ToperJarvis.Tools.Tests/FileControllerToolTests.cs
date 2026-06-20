using ToperJarvis.Tools.System;

namespace ToperJarvis.Tools.Tests;

public class FileControllerToolTests
{
    private static readonly string Home =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Resolve_sciezka_wzgledna_laczona_z_domowym()
    {
        var result = FileControllerTool.Resolve("dokumenty/plik.txt");
        Assert.StartsWith(Path.GetFullPath(Home), result);
        Assert.EndsWith("plik.txt", result);
    }

    [Fact]
    public void Resolve_pusta_sciezka_zwraca_katalog_domowy()
    {
        Assert.Equal(Path.GetFullPath(Home), FileControllerTool.Resolve(null));
    }

    [Fact]
    public void Resolve_blokuje_wyjscie_poza_katalog_domowy()
    {
        Assert.Throws<UnauthorizedAccessException>(() => FileControllerTool.Resolve("../../../Windows/System32"));
    }

    [Fact]
    public void Resolve_blokuje_sciezke_bezwzgledna_poza_domem()
    {
        // Przenośnie: rodzic katalogu domowego leży poza domem.
        var outside = Path.GetFullPath(Path.Combine(Home, ".."));
        Assert.Throws<UnauthorizedAccessException>(() => FileControllerTool.Resolve(outside));
    }

    [Fact]
    public void Resolve_blokuje_katalog_o_wspolnym_prefiksie()
    {
        // Katalog "home2" ma wspólny prefiks tekstowy z "home", ale leży poza nim.
        var sibling = Path.GetFullPath(Home).TrimEnd(Path.DirectorySeparatorChar) + "2";
        Assert.Throws<UnauthorizedAccessException>(() => FileControllerTool.Resolve(sibling));
    }
}
