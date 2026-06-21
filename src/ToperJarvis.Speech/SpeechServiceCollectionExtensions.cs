using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.Speech.Audio;
using ToperJarvis.Speech.Stt;
using ToperJarvis.Speech.Tts;
using ToperJarvis.Speech.WakeWord;

namespace ToperJarvis.Speech;

public static class SpeechServiceCollectionExtensions
{
    /// <summary>Rejestruje warstwę mowy: przechwytywanie audio, wake-word, STT, TTS.</summary>
    public static IServiceCollection AddJarvisSpeech(this IServiceCollection services)
    {
        services.AddSingleton<IAudioCapture, NAudioCapture>();
        services.AddSingleton<IAudioOutput, NAudioOutput>();
        services.AddSingleton<ISpeechToText, WhisperSpeechToText>();
        services.AddSingleton<ITextToSpeech, PiperTextToSpeech>();

        // Wybór silnika wake-word wg konfiguracji (domyślnie openWakeWord — bez klucza).
        // Nieznana wartość rzuca wyjątek zamiast cicho wybrać silnik — błąd configu nie jest maskowany.
        services.AddSingleton<IWakeWordDetector>(sp =>
        {
            var engine = sp.GetRequiredService<IOptions<JarvisOptions>>().Value.WakeWord.Engine;
            return engine.Trim().ToLowerInvariant() switch
            {
                "porcupine" => ActivatorUtilities.CreateInstance<PorcupineWakeWordDetector>(sp),
                "" or "openwakeword" => ActivatorUtilities.CreateInstance<OpenWakeWordDetector>(sp),
                _ => throw new InvalidOperationException(
                    $"Nieznany WakeWord:Engine '{engine}'. Dozwolone: 'openwakeword' (domyślny) lub 'porcupine'."),
            };
        });

        return services;
    }
}
