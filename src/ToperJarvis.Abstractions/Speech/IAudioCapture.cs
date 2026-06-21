namespace ToperJarvis.Abstractions.Speech;

/// <summary>
/// Źródło strumienia audio z mikrofonu. Dostarcza ramki próbek PCM (mono, float32 w zakresie
/// -1..1) zdarzeniem <see cref="FrameAvailable"/>. Implementacja platformowa (NAudio na Windows).
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>Częstotliwość próbkowania strumienia (Hz).</summary>
    int SampleRate { get; }

    /// <summary>Nazwa aktualnie wybranego urządzenia wejściowego (null = systemowe domyślne).</summary>
    string? SelectedDeviceName { get; }

    /// <summary>Zgłaszane dla każdej kolejnej ramki audio.</summary>
    event EventHandler<AudioFrame>? FrameAvailable;

    /// <summary>Rozpoczyna przechwytywanie z wybranego (lub domyślnego) urządzenia wejściowego.</summary>
    void Start();

    /// <summary>Zatrzymuje przechwytywanie.</summary>
    void Stop();

    /// <summary>Lista dostępnych urządzeń wejściowych (mikrofonów).</summary>
    IReadOnlyList<AudioInputDevice> GetInputDevices();

    /// <summary>
    /// Wybiera urządzenie wejściowe po nazwie (null/puste = systemowe domyślne). Jeśli
    /// przechwytywanie trwa, przełącza się na nowe urządzenie w locie bez gubienia subskrybentów.
    /// </summary>
    void SelectDevice(string? deviceName);
}

/// <summary>Urządzenie wejściowe audio (mikrofon) widoczne dla aplikacji.</summary>
/// <param name="Index">Indeks urządzenia w API platformy (NAudio WaveIn).</param>
/// <param name="Name">Nazwa produktu/urządzenia prezentowana użytkownikowi.</param>
public readonly record struct AudioInputDevice(int Index, string Name);

/// <summary>Pojedyncza ramka audio: próbki PCM mono float32 (-1..1).</summary>
public readonly struct AudioFrame(float[] samples, int sampleRate)
{
    public float[] Samples { get; } = samples;
    public int SampleRate { get; } = sampleRate;
}
