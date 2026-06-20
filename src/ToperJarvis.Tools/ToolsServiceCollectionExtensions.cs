using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.Abstractions.Tools;
using ToperJarvis.Tools.Memory;
using ToperJarvis.Tools.System;
using ToperJarvis.Tools.Web;

namespace ToperJarvis.Tools;

public static class ToolsServiceCollectionExtensions
{
    /// <summary>Rejestruje narzędzia dostępne dla LLM (każde jako <see cref="IJarvisTool"/>).</summary>
    public static IServiceCollection AddJarvisTools(this IServiceCollection services)
    {
        services.AddSingleton<IJarvisTool, SaveMemoryTool>();
        services.AddSingleton<IJarvisTool, OpenAppTool>();
        services.AddSingleton<IJarvisTool, WeatherReportTool>();
        services.AddSingleton<IJarvisTool, WebSearchTool>();

        // TODO (Krok 9-11): kolejne narzędzia (file_controller, reminder, browser_control, ...)
        return services;
    }
}
