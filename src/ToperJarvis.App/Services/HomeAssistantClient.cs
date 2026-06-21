using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.App.Services;

/// <summary>
/// Lekki klient REST Home Assistant — pobiera stan pojedynczych encji (<c>/api/states/{id}</c>).
/// Tylko skonfigurowane sensory, więc obciążenie minimalne. Wyłączony, gdy brak URL/tokenu.
/// </summary>
public sealed class HomeAssistantClient : IDisposable
{
    private readonly HttpClient? _http;
    private readonly ILogger<HomeAssistantClient> _logger;

    public HomeAssistantClient(IOptions<JarvisOptions> options, ILogger<HomeAssistantClient> logger)
    {
        _logger = logger;
        var ha = options.Value.HomeAssistant;
        if (string.IsNullOrWhiteSpace(ha.BaseUrl) || string.IsNullOrWhiteSpace(ha.Token))
            return;

        _http = new HttpClient
        {
            BaseAddress = new Uri(ha.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(8),
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ha.Token);
    }

    public bool Enabled => _http is not null;

    /// <summary>Zwraca stan encji (np. "22.2") lub null przy błędzie/niedostępności.</summary>
    public async Task<string?> GetStateAsync(string entityId, CancellationToken ct = default)
    {
        if (_http is null || string.IsNullOrWhiteSpace(entityId))
            return null;

        try
        {
            await using var stream = await _http.GetStreamAsync($"api/states/{entityId}", ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String)
            {
                var value = state.GetString();
                return string.IsNullOrWhiteSpace(value) || value == "unavailable" || value == "unknown" ? null : value;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HA: nie udało się pobrać {Entity}.", entityId);
        }

        return null;
    }

    public void Dispose() => _http?.Dispose();
}
