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
    private const string Home = "https://www.youtube.com";
    private const string Trending = "https://www.youtube.com/feed/trending";

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
        [Description("Bezpośredni adres URL filmu w serwisie YouTube (opcjonalnie).")] string? url = null)
    {
        var target = BuildTarget(action, query, url);

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return Describe(target, query);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się otworzyć YouTube ({Target}).", target);
            return "Nie udało się otworzyć YouTube.";
        }
    }

    /// <summary>Wyznacza adres do otwarcia na podstawie akcji, frazy i (zwalidowanego) URL.</summary>
    internal static string BuildTarget(string? action, string? query, string? url)
    {
        if (string.Equals(action?.Trim(), "trending", StringComparison.OrdinalIgnoreCase))
            return Trending;

        if (!string.IsNullOrWhiteSpace(url) && IsYouTubeUrl(url, out var normalized))
            return normalized;

        if (!string.IsNullOrWhiteSpace(query))
            return "https://www.youtube.com/results?search_query=" + WebUtility.UrlEncode(query.Trim());

        return Home;
    }

    /// <summary>Akceptuje wyłącznie adresy http/https w domenie youtube.com / youtu.be.</summary>
    internal static bool IsYouTubeUrl(string url, out string normalized)
    {
        normalized = Home;
        var candidate = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host.ToLowerInvariant();
        var ok = host is "youtube.com" or "youtu.be"
                 || host.EndsWith(".youtube.com", StringComparison.Ordinal);
        if (!ok)
            return false;

        normalized = uri.ToString();
        return true;
    }

    private static string Describe(string target, string? query)
    {
        if (target == Trending)
            return "Otwieram trendy YouTube.";
        if (!string.IsNullOrWhiteSpace(query))
            return $"Otwieram YouTube: {query.Trim()}.";
        return "Otwieram YouTube.";
    }
}
