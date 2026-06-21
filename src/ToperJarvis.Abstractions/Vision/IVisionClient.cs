namespace ToperJarvis.Abstractions.Vision;

/// <summary>
/// Obraz przekazywany do modelu wizji (multimodalnego). <see cref="MediaType"/> to typ MIME,
/// np. <c>image/png</c> lub <c>image/jpeg</c>.
/// </summary>
public sealed record VisionImage(byte[] Data, string MediaType);

/// <summary>
/// Klient modelu wizji (VL) — wysyła obraz(y) wraz z poleceniem tekstowym i zwraca opis/analizę.
/// Abstrakcja pozwala wskazać osobny endpoint/model wizji niezależnie od głównego LLM
/// (zob. <c>VisionOptions</c>); narzędzia (<c>screen_processor</c>, <c>file_processor</c>) zależą
/// tylko od tego interfejsu.
/// </summary>
public interface IVisionClient
{
    /// <summary>
    /// Analizuje obraz(y) modelem wizji według podanego polecenia i zwraca odpowiedź tekstową.
    /// W razie błędu zwraca przyjazny komunikat zamiast rzucać wyjątkiem (poza anulowaniem).
    /// </summary>
    Task<string> DescribeAsync(string prompt, IReadOnlyList<VisionImage> images, CancellationToken ct = default);

    /// <summary>Wygodny wariant dla pojedynczego obrazu.</summary>
    Task<string> DescribeAsync(string prompt, VisionImage image, CancellationToken ct = default)
        => DescribeAsync(prompt, [image], ct);
}
