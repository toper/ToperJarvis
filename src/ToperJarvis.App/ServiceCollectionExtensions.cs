using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.App.ViewModels;
using ToperJarvis.Core;
using ToperJarvis.Platform.Windows;
using ToperJarvis.Speech;

namespace ToperJarvis.App;

/// <summary>
/// Rejestracja usług aplikacji w kontenerze DI. Kolejne warstwy (Core, Speech, Llm, Tools,
/// Platform.Windows) będą podpinane tutaj własnymi metodami rozszerzającymi.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJarvisApp(this IServiceCollection services)
    {
        // Metryki systemu (HUD) + ViewModele + zapis ustawień użytkownika
        services.AddSingleton<Services.SystemMetricsService>();
        services.AddSingleton<Services.LocalSettingsWriter>();
        // Źródła telemetrii HUD: Home Assistant, DGX (GPU), lokalna kamera
        services.AddSingleton<Services.HomeAssistantClient>();
        services.AddSingleton<Services.DgxClient>();
        services.AddSingleton<Services.WebcamService>();
        services.AddSingleton<MainWindowViewModel>();

        // Warstwa mowy: audio, wake-word, STT, TTS
        services.AddJarvisSpeech();

        // Rdzeń: LLM (vLLM/OpenAI), prompt, orchestrator pętli głosowej, narzędzia
        services.AddJarvisCore();

        // Implementacje zależne od Windows (zrzut ekranu itp.)
        services.AddJarvisWindows();

        return services;
    }
}
