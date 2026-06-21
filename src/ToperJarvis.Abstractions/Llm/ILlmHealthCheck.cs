namespace ToperJarvis.Abstractions.Llm;

/// <summary>Wynik sprawdzenia dostępności endpointu LLM.</summary>
public sealed record LlmHealthResult(bool Healthy, string Detail);

/// <summary>
/// Sprawdza dostępność zdalnego endpointu LLM (vLLM). Pozwala wcześnie wykryć, że model jest
/// nieosiągalny (LAN, współdzielone GPU), zamiast czekać na timeout pierwszego żądania.
/// </summary>
public interface ILlmHealthCheck
{
    Task<LlmHealthResult> CheckAsync(CancellationToken ct = default);
}
