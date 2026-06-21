using System.IO.Compression;
using ClosedXML.Excel;
using ToperJarvis.Tools.Vision;

namespace ToperJarvis.Tools.Tests;

/// <summary>
/// Testy integracyjne ekstraktorów na realnych plikach tymczasowych — potwierdzają, że ciężkie
/// biblioteki (ClosedXML, System.IO.Compression) działają w runtime na net10.
/// </summary>
public class FileExtractorsTests
{
    [Fact]
    public void ListZip_zwraca_liczbe_i_nazwy_pozycji()
    {
        var path = Path.Combine(Path.GetTempPath(), $"toperjarvis_{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntry("readme.txt");
                zip.CreateEntry("dir/plik.cs");
            }

            var result = FileExtractors.ListZip(path);

            Assert.Contains("2 elementów", result);
            Assert.Contains("readme.txt", result);
            Assert.Contains("dir/plik.cs", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExtractXlsx_zwraca_nazwe_arkusza_i_wartosci_komorek()
    {
        var path = Path.Combine(Path.GetTempPath(), $"toperjarvis_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("Dane");
                sheet.Cell(1, 1).Value = "Imię";
                sheet.Cell(1, 2).Value = "Wiek";
                sheet.Cell(2, 1).Value = "Jarek";
                sheet.Cell(2, 2).Value = 42;
                workbook.SaveAs(path);
            }

            var result = FileExtractors.ExtractXlsx(path);

            Assert.Contains("Arkusz: Dane", result);
            Assert.Contains("Imię", result);
            Assert.Contains("Jarek", result);
            Assert.Contains("42", result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
