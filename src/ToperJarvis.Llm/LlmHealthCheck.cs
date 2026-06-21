using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Llm;

namespace ToperJarvis.Llm;

/// <summary>
/// Health-check endpointu LLM — odpytuje <c>{BaseUrl}/models</c> (standard API zgodnego z OpenAI)
/// i raportuje, czy model jest osiągalny.
/// </summary>
public sealed class LlmHealthCheck : ILlmHealthCheck
{
    private readonly HttpClient _http;
    private readonly string _modelsUrl;

    public LlmHealthCheck(IOptions<JarvisOptions> options)
    {
        var llm = options.Value.Llm;
        _modelsUrl = BuildModelsUrl(llm.BaseUrl);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (!string.IsNullOrWhiteSpace(llm.ApiKey) && llm.ApiKey != "not-needed")
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", llm.ApiKey);
    }

    public async Task<LlmHealthResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(_modelsUrl, ct);
            return response.IsSuccessStatusCode
                ? new LlmHealthResult(true, $"LLM osiągalny ({(int)response.StatusCode}).")
                : new LlmHealthResult(false, $"LLM zwrócił HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LlmHealthResult(false, $"LLM nieosiągalny: {ex.Message}");
        }
    }

    /// <summary>Buduje adres <c>/models</c> z bazowego URL API (kończącego się na /v1).</summary>
    internal static string BuildModelsUrl(string baseUrl) => baseUrl.TrimEnd('/') + "/models";
}
