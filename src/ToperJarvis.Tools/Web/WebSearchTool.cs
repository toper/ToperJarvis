using System.ComponentModel;
using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.Web;

/// <summary>
/// Narzędzie <c>web_search</c> — wyszukuje w DuckDuckGo (HTML endpoint), pobiera najlepsze wyniki
/// i zwraca zwięzłe streszczenie wygenerowane przez LLM.
/// </summary>
public sealed class WebSearchTool : IJarvisTool
{
    private static readonly HttpClient Http = CreateClient();

    private readonly IChatClient _chat;
    private readonly ILogger<WebSearchTool> _logger;

    public WebSearchTool(IChatClient chat, ILogger<WebSearchTool> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public string Name => "web_search";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(SearchAsync, Name,
            "Wyszukuje informacje w internecie (DuckDuckGo) i zwraca zwięzłe streszczenie wyników. " +
            "Używaj dla pytań o aktualne lub faktograficzne informacje.");

    [Description("Wyszukuje w internecie i streszcza wyniki.")]
    private async Task<string> SearchAsync(
        [Description("Zapytanie wyszukiwania.")] string query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Puste zapytanie.";

        var snippets = await FetchSnippetsAsync(query, ct);
        if (snippets.Count == 0)
            return $"Brak wyników dla: {query}.";

        var context = string.Join("\n", snippets.Take(6).Select((s, i) => $"{i + 1}. {s}"));
        var prompt =
            $"Na podstawie poniższych wyników wyszukiwania odpowiedz zwięźle (2-3 zdania) po polsku " +
            $"na zapytanie: \"{query}\".\n\nWyniki:\n{context}";

        try
        {
            var response = await _chat.GetResponseAsync(prompt, cancellationToken: ct);
            return response.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd streszczania wyników wyszukiwania.");
            return context;
        }
    }

    private async Task<List<string>> FetchSnippetsAsync(string query, CancellationToken ct)
    {
        var results = new List<string>();
        try
        {
            var url = "https://html.duckduckgo.com/html/?q=" + WebUtility.UrlEncode(query);
            var html = await Http.GetStringAsync(url, ct);

            var parser = new HtmlParser();
            var doc = await parser.ParseDocumentAsync(html, ct);

            foreach (var node in doc.QuerySelectorAll(".result__snippet"))
            {
                var text = node.TextContent.Trim();
                if (text.Length > 0)
                    results.Add(text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd pobierania wyników DuckDuckGo.");
        }

        return results;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        return client;
    }
}
