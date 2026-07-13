namespace ToperJarvis.Abstractions.Speech;

/// <summary>
/// Synteza tekstu do surowego bufora PCM (16-bit mono), bez odtwarzania. Używane przez
/// <c>CachingTextToSpeech</c> do cache'owania i odtwarzania fraz przez <c>RawPcmPlayer</c>.
/// </summary>
public interface IPcmSynthesizer
{
    /// <summary>Częstotliwość próbkowania (Hz) zwracanego bufora PCM.</summary>
    int SampleRate { get; }

    /// <summary>Syntetyzuje tekst do surowego bufora PCM (16-bit mono). Pusty tekst/brak Pipera → pusty bufor.</summary>
    Task<byte[]> SynthesizeToPcmAsync(string text, CancellationToken ct = default);
}
