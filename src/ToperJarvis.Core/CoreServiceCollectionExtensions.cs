using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.Abstractions;
using ToperJarvis.Abstractions.Memory;
using ToperJarvis.Core.Memory;
using ToperJarvis.Core.Prompting;
using ToperJarvis.Llm;
using ToperJarvis.Tools;

namespace ToperJarvis.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje rdzeń asystenta: klienta LLM, pamięć, narzędzia, dostawcę promptu oraz
    /// orchestrator pętli głosowej. Wymaga wcześniejszej rejestracji warstwy mowy (<c>AddJarvisSpeech</c>).
    /// </summary>
    public static IServiceCollection AddJarvisCore(this IServiceCollection services)
    {
        services.AddJarvisLlm();
        services.AddJarvisTools();
        services.AddSingleton<IMemoryStore, JsonMemoryStore>();
        services.AddSingleton<SystemPromptProvider>();
        services.AddSingleton<JarvisOrchestrator>();
        services.AddSingleton<IAssistantOrchestrator>(sp => sp.GetRequiredService<JarvisOrchestrator>());
        return services;
    }
}
