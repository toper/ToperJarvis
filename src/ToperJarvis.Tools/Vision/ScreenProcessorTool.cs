using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;
using ToperJarvis.Abstractions.Vision;

namespace ToperJarvis.Tools.Vision;

/// <summary>
/// Narzędzie <c>screen_processor</c> — robi zrzut ekranu i analizuje go modelem wizji (VL),
/// odpowiadając na pytanie użytkownika lub opisując zawartość ekranu.
/// </summary>
public sealed class ScreenProcessorTool : IJarvisTool
{
    private const string DefaultPrompt = "Opisz zwięźle po polsku, co widać na ekranie.";

    private readonly IScreenCapture _screen;
    private readonly IVisionClient _vision;
    private readonly ILogger<ScreenProcessorTool> _logger;

    public ScreenProcessorTool(
        IScreenCapture screen, IVisionClient vision, ILogger<ScreenProcessorTool> logger)
    {
        _screen = screen;
        _vision = vision;
        _logger = logger;
    }

    public string Name => "screen_processor";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(ProcessAsync, Name,
            "Robi zrzut ekranu i analizuje go modelem wizji. Używaj, gdy użytkownik pyta, co widać " +
            "na ekranie, prosi o opis/odczytanie zawartości ekranu lub pomoc z tym, co aktualnie wyświetla.");

    [Description("Analizuje zawartość ekranu modelem wizji.")]
    private async Task<string> ProcessAsync(
        [Description("Pytanie lub polecenie dotyczące ekranu (puste = ogólny opis).")]
        string? question = null,
        CancellationToken cancellationToken = default)
    {
        VisionImage image;
        try
        {
            image = _screen.Capture();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się wykonać zrzutu ekranu.");
            return "Nie udało się wykonać zrzutu ekranu.";
        }

        return await _vision.DescribeAsync(ResolvePrompt(question), image, cancellationToken);
    }

    /// <summary>Polecenie dla modelu: pytanie użytkownika lub domyślny opis ekranu.</summary>
    internal static string ResolvePrompt(string? question) =>
        string.IsNullOrWhiteSpace(question) ? DefaultPrompt : question.Trim();
}
