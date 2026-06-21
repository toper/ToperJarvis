using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.App.Mcp;

/// <summary>
/// Lokalny serwer MCP wystawiający narzędzia działające na tym PC zdalnemu agentowi Hermes (Hektor).
/// Gdy mózgiem jest Hektor, łączy się on po HTTP/SSE i wywołuje te narzędzia jako „ręce" na maszynie
/// użytkownika. Narzędzia wymagające zdalnych zasobów (web/research/kod) zostają po stronie Hermesa.
/// </summary>
public static class McpServerExtensions
{
    /// <summary>
    /// Narzędzia działające lokalnie (pulpit/sesja Windows) — tylko te wystawiamy Hektorowi.
    /// Pozostałe (web_search, code_helper, dev_agent, save_memory, reminder…) ma już Hermes.
    /// </summary>
    private static readonly string[] LocalToolNames =
    {
        "open_app",
        "send_message",
        "desktop_control",
        "computer_control",
        "computer_settings",
        "file_controller",
        "game_updater",
        "screen_processor",
        "browser_control",
    };

    /// <summary>Rejestruje serwer MCP (transport HTTP/SSE) i lokalne narzędzia jako <see cref="McpServerTool"/>.</summary>
    public static IServiceCollection AddJarvisMcp(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport(o =>
            {
                o.Stateless = false;        // SSE wymaga trybu stanowego
#pragma warning disable MCP9004 // EnableLegacySse jest oznaczone jako przestarzałe, ale klient Hermes używa /sse
                o.EnableLegacySse = true;
#pragma warning restore MCP9004
            });

        // Każde lokalne narzędzie jako McpServerTool, rozwiązywane leniwie z zarejestrowanych IJarvisTool.
        foreach (var name in LocalToolNames)
        {
            var toolName = name;
            services.AddSingleton<McpServerTool>(sp =>
            {
                var tool = sp.GetServices<IJarvisTool>().First(t => t.Name == toolName);
                return McpServerTool.Create(tool.AsAIFunction());
            });
        }

        return services;
    }

    /// <summary>
    /// Autoryzacja nagłówkiem <c>Authorization: Bearer &lt;token&gt;</c> dla endpointów MCP.
    /// Serwer steruje PC, więc bez tokenu odrzucamy. Pusty token = brak ochrony (ostrzeżenie loguje wołający).
    /// </summary>
    public static IApplicationBuilder UseJarvisMcpAuth(this IApplicationBuilder app, string token)
    {
        if (string.IsNullOrEmpty(token))
            return app;

        var expected = $"Bearer {token}";
        return app.Use(async (context, next) =>
        {
            if (!string.Equals(context.Request.Headers.Authorization.ToString(), expected, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            await next();
        });
    }
}
