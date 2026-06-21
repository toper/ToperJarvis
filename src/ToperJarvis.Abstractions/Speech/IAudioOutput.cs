namespace ToperJarvis.Abstractions.Speech;

/// <summary>
/// Wybór urządzenia wyjściowego audio (głośniki/słuchawki) dla odtwarzania syntezy mowy.
/// Implementacja platformowa (NAudio WaveOut na Windows).
/// </summary>
public interface IAudioOutput
{
    /// <summary>Nazwa aktualnie wybranego urządzenia wyjściowego (null = systemowe domyślne).</summary>
    string? SelectedDeviceName { get; }

    /// <summary>
    /// Numer urządzenia WaveOut do użycia przy odtwarzaniu: <c>-1</c> = systemowe domyślne
    /// (WaveMapper), w przeciwnym razie indeks urządzenia.
    /// </summary>
    int DeviceNumber { get; }

    /// <summary>Lista dostępnych urządzeń wyjściowych.</summary>
    IReadOnlyList<AudioOutputDevice> GetOutputDevices();

    /// <summary>Wybiera urządzenie wyjściowe po nazwie (null/puste = systemowe domyślne).</summary>
    void SelectDevice(string? deviceName);
}

/// <summary>Urządzenie wyjściowe audio (głośniki/słuchawki) widoczne dla aplikacji.</summary>
/// <param name="Index">Indeks urządzenia w API platformy (NAudio WaveOut).</param>
/// <param name="Name">Nazwa urządzenia prezentowana użytkownikowi.</param>
public readonly record struct AudioOutputDevice(int Index, string Name);
