using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.Tts;

/// <summary>
/// Przy starcie aplikacji wstępnie syntetyzuje frazy filler (jeśli TTS jest cache'owane), by
/// pierwsze użycie fillera w orkiestratorze było natychmiastowe. Fire-and-forget: nie opóźnia
/// startu hosta i nie rzuca wyjątkiem (błąd syntezy = brak wstępnego cache, nie awaria startu).
/// </summary>
public sealed class TtsWarmupService(ITextToSpeech tts, ILogger<TtsWarmupService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (tts is CachingTextToSpeech caching)
            _ = WarmupAsync(caching, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task WarmupAsync(CachingTextToSpeech caching, CancellationToken ct)
    {
        try
        {
            await caching.WarmupAsync(ct);
            logger.LogInformation("Wstępna synteza fraz filler zakończona.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Wstępna synteza fraz filler nie powiodła się.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
