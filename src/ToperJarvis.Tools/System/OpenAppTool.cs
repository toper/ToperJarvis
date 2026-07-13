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
        // Przeglądarki
        ["chrome"] = "chrome",
        ["google chrome"] = "chrome",
        ["edge"] = "msedge",
        ["firefox"] = "firefox",
        ["brave"] = "brave",
        ["opera"] = "opera",
        // Komunikatory / spotkania
        ["whatsapp"] = "whatsapp",
        ["telegram"] = "telegram",
        ["discord"] = "discord",
        ["slack"] = "slack",
        ["teams"] = "msteams",
        ["zoom"] = "zoom",
        ["skype"] = "skype",
        ["signal"] = "signal",
        // Media
        ["spotify"] = "spotify",
        ["vlc"] = "vlc",
        // Narzędzia / dev
        ["vscode"] = "code",
        ["visual studio code"] = "code",
        ["code"] = "code",
        ["postman"] = "postman",
        ["figma"] = "figma",
        ["blender"] = "blender",
        ["notion"] = "notion",
        ["obsidian"] = "obsidian",
        // Office
        ["word"] = "winword",
        ["excel"] = "excel",
        ["powerpoint"] = "powerpnt",
        ["libreoffice"] = "soffice",
        // Gry
        ["steam"] = "steam",
        ["epic"] = "EpicGamesLauncher",
        ["epic games"] = "EpicGamesLauncher",
        // System / wbudowane
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
        ["menedżer zadań"] = "taskmgr",
        ["menedzer zadan"] = "taskmgr",
        ["task manager"] = "taskmgr",
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

        var raw = appName.Trim();
        var target = Resolve(raw);

        // 1. Bezpośrednio przez powłokę — działa dla URL, protokołów (ms-settings:), programów na PATH
        //    oraz wpisanych w rejestrze App Paths (chrome, msedge, firefox itp.).
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return $"Otwieram: {appName}.";
        }
        catch (Win32Exception ex)
        {
            // Najczęściej „nie znaleziono pliku" — cel spoza PATH/App Paths (Spotify, Discord, Teams,
            // Word, Steam…). Te aplikacje są jednak w menu Start → próbujemy fallbacku poniżej.
            _logger.LogDebug(ex, "Powłoka nie znalazła {Target} — próbuję menu Start.", target);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się otworzyć {App} (cel: {Target}).", appName, target);
            return $"Nie udało się otworzyć: {appName}.";
        }

        // 2. Fallback: indeks menu Start (Get-StartApps) → explorer shell:AppsFolder\<AUMID>.
        //    Obejmuje aplikacje Store i klasyczne, których powłoka nie zna po gołej nazwie.
        if (OperatingSystem.IsWindows() && TryLaunchViaStartMenu(raw, target))
            return $"Otwieram: {appName}.";

        _logger.LogWarning("Nie znaleziono {App} (cel: {Target}) ani przez powłokę, ani w menu Start.", appName, target);
        return $"Nie udało się otworzyć: {appName}.";
    }

    /// <summary>
    /// Uruchamia aplikację przez indeks menu Start: <c>Get-StartApps</c> zwraca AppID/AUMID (Win32 i UWP),
    /// a <c>explorer.exe shell:AppsFolder\&lt;AUMID&gt;</c> ją odpala. To deterministyczny odpowiednik
    /// „wpisz nazwę w menu Start" z oryginału — bez symulacji klawiatury.
    /// </summary>
    private bool TryLaunchViaStartMenu(string rawName, string resolvedTarget)
    {
        var appId = ResolveStartMenuAppId([rawName, StripToSearchTerm(resolvedTarget)]);
        if (appId is null)
            return false;

        try
        {
            // AUMID aplikacji desktopowych bywa ze spacjami — cudzysłów chroni przed rozbiciem argumentu.
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"shell:AppsFolder\\{appId}\"") { UseShellExecute = true });
            _logger.LogInformation("Otwarto przez menu Start: {Name} → {AppId}.", rawName, appId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się uruchomić przez AppsFolder ({AppId}).", appId);
            return false;
        }
    }

    /// <summary>
    /// Pyta PowerShell o pierwszą aplikację z menu Start, której nazwa pasuje do któregoś z kandydatów
    /// (dopasowanie „zawiera", bez uwzględniania wielkości liter). Zwraca jej AppID/AUMID albo null.
    /// </summary>
    private static string? ResolveStartMenuAppId(IReadOnlyList<string> candidates)
    {
        // Sanityzacja: tylko litery/cyfry/spacja — kandydaci trafiają wprost do skryptu PowerShell.
        var terms = candidates
            .Select(c => new string((c ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length == 0)
            return null;

        var list = string.Join(",", terms.Select(t => "'" + t + "'"));
        var script =
            "$c=@(" + list + "); " +
            "Get-StartApps | Where-Object { $n=$_.Name; ($c | Where-Object { $n -like \"*$_*\" }).Count -gt 0 } " +
            "| Select-Object -First 1 -ExpandProperty AppID";

        var psi = new ProcessStartInfo("powershell")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(7_000))
                return null;

            var appId = output.Split('\n', '\r').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return string.IsNullOrWhiteSpace(appId) ? null : appId;
        }
        catch (Exception)
        {
            return null; // brak powershell / błąd uruchomienia — fallback po prostu się nie udał
        }
    }

    /// <summary>Sprowadza cel do hasła wyszukiwania w menu Start: ścieżka/exe → sama nazwa pliku bez rozszerzenia.</summary>
    private static string StripToSearchTerm(string target)
    {
        if (target.Contains("://", StringComparison.Ordinal) || target.EndsWith(':'))
            return target;
        var name = Path.GetFileNameWithoutExtension(target);
        return string.IsNullOrWhiteSpace(name) ? target : name;
    }

    internal static string Resolve(string name)
    {
        if (LooksLikeUrl(name))
            return name.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? name : "https://" + name;

        if (WebApps.TryGetValue(name, out var url))
            return url;

        if (Aliases.TryGetValue(name, out var exe))
            return exe;

        return name; // fallback — powłoka spróbuje sama
    }

    private static readonly HashSet<string> NonUrlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".msi", ".lnk", ".ps1", ".sh", ".scr",
    };

    private static bool LooksLikeUrl(string name)
    {
        if (name.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (name.Contains(' ') || !name.Contains('.'))
            return false;

        var ext = Path.GetExtension(name);
        if (NonUrlExtensions.Contains(ext))
            return false;

        // Domena: część po ostatniej kropce wygląda jak TLD (same litery, min. 2 znaki).
        var tld = ext.TrimStart('.');
        return tld.Length >= 2 && tld.All(char.IsLetter);
    }
}
