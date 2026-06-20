using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.Abstractions.Tools;
using ToperJarvis.Tools.Dev;
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
        services.AddSingleton<IJarvisTool, FileControllerTool>();
        services.AddSingleton<IJarvisTool, ReminderTool>();
        services.AddSingleton<IJarvisTool, YouTubeVideoTool>();
        services.AddSingleton<IJarvisTool, ComputerSettingsTool>();
        services.AddSingleton<IJarvisTool, DesktopControlTool>();
        services.AddSingleton<IJarvisTool, ComputerControlTool>();
        services.AddSingleton<IJarvisTool, GameUpdaterTool>();
        services.AddSingleton<IJarvisTool, SendMessageTool>();
        services.AddSingleton<IJarvisTool, BrowserControlTool>();
        services.AddSingleton<IJarvisTool, CodeHelperTool>();
        services.AddSingleton<IJarvisTool, DevAgentTool>();

        // TODO (Krok 11): kolejne narzędzia z wizją (screen_processor, file_processor) — wymagają IVisionClient.
        return services;
    }
}
