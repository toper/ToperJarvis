using Microsoft.Extensions.AI;

namespace ToperJarvis.Abstractions.Tools;

/// <summary>
/// Narzędzie dostępne dla LLM (tool-calling). Każde narzędzie udostępnia swoją definicję jako
/// <see cref="AIFunction"/>, którą orchestrator dołącza do <c>ChatOptions.Tools</c>; faktyczne
/// wywołanie obsługuje pipeline function-invocation z Microsoft.Extensions.AI.
/// </summary>
public interface IJarvisTool
{
    /// <summary>Stała nazwa narzędzia widoczna dla modelu (np. „open_app").</summary>
    string Name { get; }

    /// <summary>Buduje definicję funkcji (schemat + delegat wykonujący) dla LLM.</summary>
    AIFunction AsAIFunction();
}
