using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Llm;

namespace ToperJarvis.Llm;

/// <summary>
/// Przy starcie aplikacji sprawdza dostępność LLM i loguje wynik — wczesna diagnostyka, gdy
/// zdalny vLLM jest nieosiągalny. Nie blokuje startu i nie rzuca wyjątkiem.
/// </summary>
public sealed class LlmHealthLogger(ILlmHealthCheck healthCheck, ILogger<LlmHealthLogger> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await healthCheck.CheckAsync(cancellationToken);
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
