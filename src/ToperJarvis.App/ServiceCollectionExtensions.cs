using Microsoft.Extensions.DependencyInjection;
using ToperJarvis.App.ViewModels;

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

        // TODO (Krok 2-6): services.AddJarvisSpeech();  (audio, wake-word, STT, TTS)
        // TODO (Krok 5):   services.AddJarvisLlm();      (klient vLLM/OpenAI)
        // TODO (Krok 7):   services.AddJarvisCore();     (orchestrator, pamięć)
        // TODO (Krok 9+):  services.AddJarvisTools();    (narzędzia)
        // TODO:            services.AddJarvisWindows();  (implementacje platformowe)

        return services;
    }
}
