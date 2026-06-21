using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ToperJarvis.Abstractions.Vision;

namespace ToperJarvis.Platform.Windows;

/// <summary>
/// Zrzut ekranu głównego monitora przez GDI (<see cref="Graphics.CopyFromScreen(int, int, int, int, Size)"/>),
/// zwrócony jako PNG. Rozmiar pulpitu pobierany przez <c>GetSystemMetrics</c> (bez zależności od WinForms).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsScreenCapture : IScreenCapture
{
    private const int SmCxScreen = 0; // szerokość głównego monitora
    private const int SmCyScreen = 1; // wysokość głównego monitora

    public VisionImage Capture()
    {
        var width = GetSystemMetrics(SmCxScreen);
        var height = GetSystemMetrics(SmCyScreen);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Nie udało się odczytać rozmiaru ekranu.");

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(0, 0, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new VisionImage(stream.ToArray(), "image/png");
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
