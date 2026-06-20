using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.Abstractions.Tools;
using ToperJarvis.Tools.Memory;

namespace ToperJarvis.Tools;

public static class ToolsServiceCollectionExtensions
{
    /// <summary>Rejestruje narzędzia dostępne dla LLM (każde jako <see cref="IJarvisTool"/>).</summary>
    public static IServiceCollection AddJarvisTools(this IServiceCollection services)
    {
        services.AddSingleton<IJarvisTool, SaveMemoryTool>();

        // TODO (Krok 9-11): kolejne 16 narzędzi (open_app, web_search, browser_control, ...)
        return services;
    }
}
