using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.App.ViewModels;
using ToperJarvis.Core;
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
        // ViewModele
        services.AddSingleton<MainWindowViewModel>();

        // Warstwa mowy: audio, wake-word, STT, TTS
        services.AddJarvisSpeech();

        // Rdzeń: LLM (vLLM/OpenAI), prompt, orchestrator pętli głosowej
        services.AddJarvisCore();

        // TODO (Krok 9+):  services.AddJarvisTools();    (narzędzia)
        // TODO:            services.AddJarvisWindows();  (implementacje platformowe)

        return services;
    }
}
