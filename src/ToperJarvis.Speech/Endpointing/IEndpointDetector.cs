namespace ToperJarvis.Speech.Endpointing;

/// <summary>
/// Wykrywa koniec wypowiedzi użytkownika. Zwraca null w trakcie nasłuchu,
/// a kompletny bufor wypowiedzi (float[] 16 kHz mono) gdy tura się kończy.
/// </summary>
public interface IEndpointDetector
{
    float[]? Process(ReadOnlySpan<float> chunk);
    void Reset();
}
