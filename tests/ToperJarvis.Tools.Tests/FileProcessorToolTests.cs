using ToperJarvis.Tools.Vision;

namespace ToperJarvis.Tools.Tests;

public class FileProcessorToolTests
{
    [Theory]
    [InlineData(".png", "Image")]
    [InlineData(".JPG", "Image")] // bez rozróżniania wielkości liter
    [InlineData(".webp", "Image")]
    [InlineData(".txt", "Text")]
    [InlineData(".cs", "Text")]
    [InlineData(".json", "Text")]
    [InlineData(".pdf", "Document")]
    [InlineData(".docx", "Document")]
    [InlineData(".XLSX", "Document")] // case-insensitive
    [InlineData(".pptx", "Document")]
    [InlineData(".zip", "Archive")]
    [InlineData(".mp3", "Media")]
    [InlineData(".MP4", "Media")] // wideo też jako media (transkrypcja audio)
    [InlineData(".flac", "Media")]
    [InlineData(".rar", "Unsupported")] // inne archiwa odłożone
    [InlineData("", "Unsupported")]
    public void Classify_rozpoznaje_typ_pliku(string extension, string expected)
    {
        Assert.Equal(expected, FileProcessorTool.Classify(extension).ToString());
    }

    [Fact]
    public void ResolveImagePrompt_pusty_daje_domyslny_opis()
    {
        Assert.Equal(
            "Opisz szczegółowo po polsku, co przedstawia ten obraz. Jeśli zawiera tekst, odczytaj go.",
            FileProcessorTool.ResolveImagePrompt(null));
    }

    [Fact]
    public void ResolveTextPrompt_pusty_daje_domyslne_streszczenie()
    {
        Assert.Equal("Streść zwięźle po polsku zawartość tego pliku.", FileProcessorTool.ResolveTextPrompt("  "));
    }

    [Theory]
    [InlineData("odczytaj tekst", "odczytaj tekst")]
    [InlineData("  co to za błąd?  ", "co to za błąd?")]
    public void ResolvePrompt_zwraca_pytanie_uzytkownika(string question, string expected)
    {
        Assert.Equal(expected, FileProcessorTool.ResolveImagePrompt(question));
        Assert.Equal(expected, FileProcessorTool.ResolveTextPrompt(question));
    }
}
