using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.Speech.Audio;
using ToperJarvis.Speech.Endpointing;
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

        // TTS: konkret Piper zarejestrowany osobno, by dekorator cache mógł go opakować bez
        // duplikowania procesu Pipera. Cache włączany/wyłączany wg configu (Tts:CacheEnabled).
        services.AddSingleton<PiperTextToSpeech>();
        services.AddSingleton<IPcmSynthesizer>(sp => sp.GetRequiredService<PiperTextToSpeech>());
        services.AddSingleton<ITextToSpeech>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<JarvisOptions>>().Value.Tts;
            if (!opts.CacheEnabled)
                return sp.GetRequiredService<PiperTextToSpeech>();

            return new CachingTextToSpeech(
                sp.GetRequiredService<IPcmSynthesizer>(),
                sp.GetRequiredService<IAudioOutput>(),
                opts,
                sp.GetRequiredService<ILogger<CachingTextToSpeech>>());
        });
        services.AddHostedService<TtsWarmupService>();

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

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<JarvisOptions>>().Value;
            return new SileroVadModel(o.SmartTurn.SileroVadPath, sp.GetRequiredService<ILogger<SileroVadModel>>());
        });
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<JarvisOptions>>().Value;
            return new SmartTurnModel(o.SmartTurn.ModelPath, sp.GetRequiredService<ILogger<SmartTurnModel>>());
        });
        services.AddSingleton<IEndpointDetectorFactory, EndpointDetectorFactory>();

        return services;
    }
}
