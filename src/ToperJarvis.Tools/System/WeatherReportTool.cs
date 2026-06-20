using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.AI;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>weather_report</c> — otwiera w przeglądarce wyszukiwanie pogody dla miasta.
/// </summary>
public sealed class WeatherReportTool : IJarvisTool
{
    public string Name => "weather_report";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Report, Name,
            "Pokazuje pogodę dla wskazanego miasta (otwiera wyszukiwarkę pogody w przeglądarce).");

    [Description("Otwiera prognozę pogody dla miasta.")]
    private string Report(
        [Description("Nazwa miasta.")] string city,
        [Description("Opcjonalny zakres czasu, np. 'jutro', 'weekend'.")] string? time = null)
    {
        if (string.IsNullOrWhiteSpace(city))
            return "Nie podano miasta.";

        var query = WebUtility.UrlEncode($"pogoda {city} {time}".Trim());
        var url = $"https://www.google.com/search?q={query}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return $"Pokazuję pogodę dla: {city}.";
        }
        catch (Exception)
        {
            return $"Nie udało się otworzyć prognozy pogody dla: {city}.";
        }
    }
}
