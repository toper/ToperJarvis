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
        services.AddSingleton<ISpeechToText, WhisperSpeechToText>();
        services.AddSingleton<ITextToSpeech, PiperTextToSpeech>();

        // Wybór silnika wake-word wg konfiguracji (domyślnie openWakeWord — bez klucza).
        services.AddSingleton<IWakeWordDetector>(sp =>
        {
            var engine = sp.GetRequiredService<IOptions<JarvisOptions>>().Value.WakeWord.Engine;
            return engine.Equals("porcupine", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<PorcupineWakeWordDetector>(sp)
                : ActivatorUtilities.CreateInstance<OpenWakeWordDetector>(sp);
        });

        return services;
    }
}
