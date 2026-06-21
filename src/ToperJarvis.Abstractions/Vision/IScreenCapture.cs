namespace ToperJarvis.Abstractions.Vision;

/// <summary>
/// Przechwytywanie obrazu ekranu. Implementacja zależna od platformy
/// (zob. <c>ToperJarvis.Platform.Windows</c>). Zwraca <see cref="VisionImage"/> gotowy do
/// przekazania do <see cref="IVisionClient"/>.
/// </summary>
public interface IScreenCapture
{
    /// <summary>Przechwytuje zrzut ekranu głównego monitora jako obraz PNG.</summary>
    VisionImage Capture();
}
