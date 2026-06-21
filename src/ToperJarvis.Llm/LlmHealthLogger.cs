using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Llm;

namespace ToperJarvis.Llm;

/// <summary>
/// Przy starcie aplikacji sprawdza dostępność LLM i loguje wynik — wczesna diagnostyka, gdy
/// zdalny vLLM jest nieosiągalny. Sprawdzenie biegnie w tle, więc NIE opóźnia startu hosta
/// (health-check nieosiągalnego LLM może trwać do timeoutu). Nie rzuca wyjątkiem.
/// </summary>
public sealed class LlmHealthLogger(ILlmHealthCheck healthCheck, ILogger<LlmHealthLogger> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget: nie blokujemy startu hosta czekaniem na sieć.
        _ = CheckAndLogAsync(cancellationToken);
        return Task.CompletedTask;
    }

    private async Task CheckAndLogAsync(CancellationToken ct)
    {
        try
        {
            var result = await healthCheck.CheckAsync(ct);
            if (result.Healthy)
                logger.LogInformation("Health-check LLM: {Detail}", result.Detail);
            else
                logger.LogWarning("Health-check LLM: {Detail}", result.Detail);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health-check LLM nie powiódł się.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
