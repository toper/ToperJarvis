namespace ToperJarvis.Abstractions.Speech;

/// <summary>Rozpoznawanie mowy (offline). Zamienia próbki PCM na tekst.</summary>
public interface ISpeechToText
{
    /// <summary>
    /// Transkrybuje wypowiedź (mono float32, częstotliwość zgodna z konfiguracją audio).
    /// Zwraca rozpoznany tekst (może być pusty, gdy nic nie rozpoznano).
    /// </summary>
    Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default);
}
