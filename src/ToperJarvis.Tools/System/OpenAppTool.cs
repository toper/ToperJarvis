using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>open_app</c> — uruchamia aplikację, stronę WWW lub program po nazwie.
/// Obsługuje aliasy popularnych aplikacji, adresy URL oraz fallback przez powłokę systemową.
/// </summary>
public sealed class OpenAppTool : IJarvisTool
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "chrome",
        ["google chrome"] = "chrome",
        ["edge"] = "msedge",
        ["firefox"] = "firefox",
        ["notatnik"] = "notepad",
        ["notepad"] = "notepad",
        ["kalkulator"] = "calc",
        ["calculator"] = "calc",
        ["eksplorator"] = "explorer",
        ["explorer"] = "explorer",
        ["paint"] = "mspaint",
        ["cmd"] = "cmd",
        ["terminal"] = "wt",
        ["powershell"] = "powershell",
        ["word"] = "winword",
        ["excel"] = "excel",
        ["spotify"] = "spotify",
        ["discord"] = "discord",
        ["ustawienia"] = "ms-settings:",
        ["settings"] = "ms-settings:",
    };

    private static readonly Dictionary<string, string> WebApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://www.youtube.com",
        ["gmail"] = "https://mail.google.com",
        ["github"] = "https://github.com",
        ["chatgpt"] = "https://chat.openai.com",
        ["maps"] = "https://maps.google.com",
        ["mapy"] = "https://maps.google.com",
    };

    private readonly ILogger<OpenAppTool> _logger;

    public OpenAppTool(ILogger<OpenAppTool> logger) => _logger = logger;

    public string Name => "open_app";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Open, Name,
            "Otwiera aplikację, program lub stronę internetową po nazwie (np. 'chrome', 'kalkulator', " +
            "'youtube', albo adres URL).");

    [Description("Otwiera aplikację lub stronę.")]
    private string Open([Description("Nazwa aplikacji, programu lub adres strony.")] string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return "Nie podano nazwy aplikacji.";

        var target = Resolve(appName.Trim());
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return $"Otwieram: {appName}.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się otworzyć {App} (cel: {Target}).", appName, target);
            return $"Nie udało się otworzyć: {appName}.";
        }
    }

    private static string Resolve(string name)
    {
        if (LooksLikeUrl(name))
            return name.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? name : "https://" + name;

        if (WebApps.TryGetValue(name, out var url))
            return url;

        if (Aliases.TryGetValue(name, out var exe))
            return exe;

        return name; // fallback — powłoka spróbuje sama
    }

    private static bool LooksLikeUrl(string name) =>
        name.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        name.Contains('.') && !name.Contains(' ');
}
