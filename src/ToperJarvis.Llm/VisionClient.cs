using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Vision;

namespace ToperJarvis.Llm;

/// <summary>
/// Klient modelu wizji oparty o API zgodne z OpenAI (vLLM). Woła <c>/chat/completions</c>
/// bezpośrednio przez HTTP, bo wymaga specyficznego dla vLLM parametru
/// <c>chat_template_kwargs: {"enable_thinking": false}</c> — bez niego model rozumujący
/// (Qwen3) zużywa cały budżet tokenów na myślenie i zwraca puste <c>content</c>.
/// Endpoint/model/klucz pochodzą z <see cref="VisionOptions"/>; puste pola = wartości z LLM.
/// </summary>
public sealed class VisionClient : IVisionClient
{
    private readonly HttpClient _http;
    private readonly ILogger<VisionClient> _logger;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly int _maxTokens;

    public VisionClient(IOptions<JarvisOptions> options, ILogger<VisionClient> logger)
    {
        var o = options.Value;
        var vision = o.Vision;

        var baseUrl = string.IsNullOrWhiteSpace(vision.BaseUrl) ? o.Llm.BaseUrl : vision.BaseUrl;
        _model = string.IsNullOrWhiteSpace(vision.Model) ? o.Llm.Model : vision.Model;
        var apiKey = string.IsNullOrWhiteSpace(vision.ApiKey) ? o.Llm.ApiKey : vision.ApiKey;
        _maxTokens = vision.MaxTokens > 0 ? vision.MaxTokens : 1024;
        var timeout = vision.TimeoutSeconds > 0 ? vision.TimeoutSeconds : 120;

        _endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        _logger = logger;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
        if (!string.IsNullOrWhiteSpace(apiKey) && apiKey != "not-needed")
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> DescribeAsync(
        string prompt, IReadOnlyList<VisionImage> images, CancellationToken ct = default)
    {
        if (images is null || images.Count == 0)
            return "Brak obrazu do analizy.";

        try
        {
            var requestJson = BuildRequestJson(_model, prompt, images, _maxTokens);
            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(_endpoint, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Model wizji zwrócił HTTP {Status}: {Body}",
                    (int)response.StatusCode, Truncate(body));
                return $"Błąd modelu wizji (HTTP {(int)response.StatusCode}).";
            }

            var text = ParseContent(body);
            return string.IsNullOrWhiteSpace(text) ? "Model wizji nie zwrócił opisu obrazu." : text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd wywołania modelu wizji ({Endpoint}).", _endpoint);
            return "Nie udało się przeanalizować obrazu.";
        }
    }

    /// <summary>
    /// Buduje treść żądania chat-completions z polem multimodalnym (tekst + obrazy jako data URI)
    /// oraz wyłączonym myśleniem modelu.
    /// </summary>
    internal static string BuildRequestJson(
        string model, string prompt, IReadOnlyList<VisionImage> images, int maxTokens)
    {
        var parts = new List<object> { new { type = "text", text = prompt } };
        foreach (var image in images)
        {
            var dataUri = $"data:{image.MediaType};base64,{Convert.ToBase64String(image.Data)}";
            parts.Add(new { type = "image_url", image_url = new { url = dataUri } });
        }

        var body = new
        {
            model,
            max_tokens = maxTokens,
            temperature = 0,
            messages = new[] { new { role = "user", content = parts } },
            chat_template_kwargs = new { enable_thinking = false },
        };

        return JsonSerializer.Serialize(body);
    }

    /// <summary>
    /// Wyciąga <c>choices[0].message.content</c> z odpowiedzi API. Zwraca <c>null</c>, gdy treści brak
    /// (np. model zwrócił samo rozumowanie) — wołający zamienia to na przyjazny komunikat.
    /// </summary>
    internal static string? ParseContent(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static string Truncate(string text, int max = 500) =>
        text.Length <= max ? text : text[..max] + "…";
}
