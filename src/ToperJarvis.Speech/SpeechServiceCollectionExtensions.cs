using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IWakeWordDetector, PorcupineWakeWordDetector>();
        services.AddSingleton<ISpeechToText, WhisperSpeechToText>();
        services.AddSingleton<ITextToSpeech, PiperTextToSpeech>();
        return services;
    }
}
