using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Vision;

namespace ToperJarvis.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje klienta LLM (<see cref="IChatClient"/>) wskazującego na zdalny vLLM przez API
    /// zgodne z OpenAI. Pipeline zawiera automatyczne wywoływanie narzędzi (function invocation),
    /// dzięki czemu pętla tool-callingu jest obsługiwana przez Microsoft.Extensions.AI.
    /// </summary>
    public static IServiceCollection AddJarvisLlm(this IServiceCollection services)
    {
        services.AddSingleton<IChatClient>(sp =>
        {
            var llm = sp.GetRequiredService<IOptions<JarvisOptions>>().Value.Llm;

            var openAi = new OpenAIClient(
                new ApiKeyCredential(string.IsNullOrWhiteSpace(llm.ApiKey) ? "not-needed" : llm.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(llm.BaseUrl) });

            return openAi.GetChatClient(llm.Model)
                .AsIChatClient()
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        });

        services.AddSingleton<IVisionClient, VisionClient>();

        return services;
    }
}
