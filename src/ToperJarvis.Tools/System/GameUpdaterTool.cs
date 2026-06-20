using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>game_updater</c> — instaluje/aktualizuje/uruchamia gry Steam przez protokół
/// <c>steam://</c>. Rozpoznaje popularne tytuły po nazwie (mapowanie na AppID).
/// </summary>
public sealed class GameUpdaterTool : IJarvisTool
{
    private static readonly Dictionary<string, string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2"] = "730", ["counter-strike 2"] = "730", ["counter strike"] = "730",
        ["dota 2"] = "570", ["dota"] = "570",
        ["gta v"] = "271590", ["gta 5"] = "271590", ["grand theft auto v"] = "271590",
        ["pubg"] = "578080",
        ["rust"] = "252490",
        ["team fortress 2"] = "440", ["tf2"] = "440",
        ["apex legends"] = "1172470", ["apex"] = "1172470",
        ["elden ring"] = "1245620",
        ["cyberpunk 2077"] = "1091500", ["cyberpunk"] = "1091500",
    };

    public string Name => "game_updater";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Execute, Name,
            "Zarządza grami Steam: 'install'/'update' (instaluje/aktualizuje), 'run' (uruchamia), " +
            "'list' (otwiera bibliotekę). Podaj nazwę gry lub AppID.");

    [Description("Zarządza grami Steam.")]
    private string Execute(
        [Description("Akcja: install, update, run lub list.")] string action,
        [Description("Nazwa gry (np. 'cs2') — zamieniana na AppID.")] string? gameName = null,
        [Description("AppID Steam (jeśli znany).")] string? appId = null)
    {
        var act = (action ?? string.Empty).Trim().ToLowerInvariant();

        if (act == "list")
            return Launch("steam://open/games", "Otwieram bibliotekę Steam.");

        var id = ResolveAppId(gameName, appId);
        if (id is null)
            return $"Nie rozpoznano gry: {gameName ?? appId}.";

        return act switch
        {
            "install" or "update" => Launch($"steam://install/{id}", $"Instaluję/aktualizuję grę (AppID {id})."),
            "run" or "play" => Launch($"steam://run/{id}", $"Uruchamiam grę (AppID {id})."),
            _ => $"Nieobsługiwana akcja: {action}.",
        };
    }

    /// <summary>Zwraca AppID z jawnego parametru lub mapowania nazwy. Null jeśli nieznane.</summary>
    internal static string? ResolveAppId(string? gameName, string? appId)
    {
        if (!string.IsNullOrWhiteSpace(appId) && appId.All(char.IsDigit))
            return appId;

        if (!string.IsNullOrWhiteSpace(gameName) && KnownGames.TryGetValue(gameName.Trim(), out var id))
            return id;

        return null;
    }

    private static string Launch(string uri, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return message;
        }
        catch (Exception)
        {
            return "Nie udało się uruchomić Steam.";
        }
    }
}
