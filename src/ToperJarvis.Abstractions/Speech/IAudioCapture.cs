namespace ToperJarvis.Abstractions.Speech;

/// <summary>
/// Źródło strumienia audio z mikrofonu. Dostarcza ramki próbek PCM (mono, float32 w zakresie
/// -1..1) zdarzeniem <see cref="FrameAvailable"/>. Implementacja platformowa (NAudio na Windows).
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>Częstotliwość próbkowania strumienia (Hz).</summary>
    int SampleRate { get; }

    /// <summary>Zgłaszane dla każdej kolejnej ramki audio.</summary>
    event EventHandler<AudioFrame>? FrameAvailable;

    /// <summary>Rozpoczyna przechwytywanie z domyślnego urządzenia wejściowego.</summary>
    void Start();

    /// <summary>Zatrzymuje przechwytywanie.</summary>
    void Stop();
}

/// <summary>Pojedyncza ramka audio: próbki PCM mono float32 (-1..1).</summary>
public readonly struct AudioFrame(float[] samples, int sampleRate)
{
    public float[] Samples { get; } = samples;
    public int SampleRate { get; } = sampleRate;
}
