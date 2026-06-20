using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.Web;

/// <summary>
/// Narzędzie <c>youtube_video</c> — otwiera YouTube w przeglądarce: odtwarza/wyszukuje film
/// po frazie lub adresie URL, albo pokazuje trendy.
/// </summary>
public sealed class YouTubeVideoTool : IJarvisTool
{
    private readonly ILogger<YouTubeVideoTool> _logger;

    public YouTubeVideoTool(ILogger<YouTubeVideoTool> logger) => _logger = logger;

    public string Name => "youtube_video";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Execute, Name,
            "Otwiera YouTube: odtwarza lub wyszukuje film po frazie albo adresie URL ('play'/'search'), " +
            "lub pokazuje trendy ('trending').");

    [Description("Otwiera YouTube.")]
    private string Execute(
        [Description("Akcja: play, search lub trending.")] string action = "search",
        [Description("Fraza do wyszukania (dla play/search).")] string? query = null,
        [Description("Bezpośredni adres URL filmu (opcjonalnie).")] string? url = null)
    {
        var target = (action ?? "search").Trim().ToLowerInvariant() switch
        {
            "trending" => "https://www.youtube.com/feed/trending",
            _ when !string.IsNullOrWhiteSpace(url) => NormalizeUrl(url!),
            _ when !string.IsNullOrWhiteSpace(query) =>
                "https://www.youtube.com/results?search_query=" + WebUtility.UrlEncode(query),
            _ => "https://www.youtube.com",
        };

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return query is not null ? $"Otwieram YouTube: {query}." : "Otwieram YouTube.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się otworzyć YouTube ({Target}).", target);
            return "Nie udało się otworzyć YouTube.";
        }
    }

    private static string NormalizeUrl(string url) =>
        url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
}
