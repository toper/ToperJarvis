using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ToperJarvis.Tools.Vision;

/// <summary>
/// Ekstrakcja tekstu/zawartości z dokumentów i archiwów dla <see cref="FileProcessorTool"/>.
/// Każda metoda jest synchroniczna (operacje na plikach lokalnych) i może rzucić wyjątkiem
/// formatu — wołający (narzędzie) opakowuje wywołanie w obsługę błędów.
/// </summary>
internal static class FileExtractors
{
    private const int MaxArchiveEntries = 50;
    private const int MaxSheetRows = 50;

    /// <summary>Wyciąga tekst ze wszystkich stron PDF (PdfPig).</summary>
    public static string ExtractPdf(string path)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(path);
        foreach (var page in document.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    /// <summary>Wyciąga tekst akapitów z pliku .docx (OpenXML).</summary>
    public static string ExtractDocx(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return "";

        var sb = new StringBuilder();
        foreach (var paragraph in body.Descendants<W.Paragraph>())
            sb.AppendLine(paragraph.InnerText);
        return sb.ToString();
    }

    /// <summary>Wyciąga tekst slajdów z pliku .pptx (OpenXML).</summary>
    public static string ExtractPptx(string path)
    {
        using var document = PresentationDocument.Open(path, false);
        var slideParts = document.PresentationPart?.SlideParts;
        if (slideParts is null)
            return "";

        var sb = new StringBuilder();
        var index = 1;
        foreach (var slidePart in slideParts)
        {
            sb.AppendLine($"--- Slajd {index++} ---");
            foreach (var text in slidePart.Slide.Descendants<A.Text>())
                sb.AppendLine(text.Text);
        }
        return sb.ToString();
    }

    /// <summary>Podgląd arkuszy .xlsx — nagłówek arkusza i pierwsze wiersze (ClosedXML).</summary>
    public static string ExtractXlsx(string path)
    {
        var sb = new StringBuilder();
        using var workbook = new XLWorkbook(path);
        foreach (var sheet in workbook.Worksheets)
        {
            sb.AppendLine($"--- Arkusz: {sheet.Name} ---");
            var used = sheet.RangeUsed();
            if (used is null)
                continue;

            var row = 0;
            foreach (var line in used.Rows())
            {
                if (row++ >= MaxSheetRows)
                {
                    sb.AppendLine("…");
                    break;
                }
                sb.AppendLine(string.Join("\t", line.Cells().Select(c => c.GetString())));
            }
        }
        return sb.ToString();
    }

    /// <summary>Listuje zawartość archiwum .zip (do <see cref="MaxArchiveEntries"/> pozycji).</summary>
    public static string ListZip(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries;

        var sb = new StringBuilder();
        sb.AppendLine($"Archiwum zawiera {entries.Count} elementów:");
        foreach (var entry in entries.Take(MaxArchiveEntries))
            sb.AppendLine(entry.FullName);
        if (entries.Count > MaxArchiveEntries)
            sb.AppendLine($"… i jeszcze {entries.Count - MaxArchiveEntries}.");
        return sb.ToString();
    }
}
