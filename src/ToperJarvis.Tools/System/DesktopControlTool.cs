using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.AI;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>desktop_control</c> — operacje na pulpicie. Obecnie: ustawianie tapety
/// (<c>set_wallpaper</c>) ze wskazanego pliku obrazu.
/// </summary>
public sealed class DesktopControlTool : IJarvisTool
{
    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    public string Name => "desktop_control";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Execute, Name,
            "Operacje na pulpicie. Akcja 'set_wallpaper' ustawia tapetę z pliku obrazu (path).");

    [Description("Wykonuje operację na pulpicie.")]
    private string Execute(
        [Description("Akcja, np. 'set_wallpaper'.")] string action,
        [Description("Ścieżka pliku obrazu (dla set_wallpaper).")] string? path = null)
    {
        if (!string.Equals(action?.Trim(), "set_wallpaper", StringComparison.OrdinalIgnoreCase))
            return $"Nieobsługiwana akcja: {action}.";

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return "Nie znaleziono pliku obrazu.";

        var ok = SystemParametersInfo(SpiSetDeskWallpaper, 0, Path.GetFullPath(path),
            SpifUpdateIniFile | SpifSendChange);

        return ok ? "Ustawiono tapetę." : "Nie udało się ustawić tapety.";
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
}
