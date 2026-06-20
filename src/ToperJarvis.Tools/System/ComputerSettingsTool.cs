using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.AI;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>computer_settings</c> — sterowanie głośnością systemu (głośniej/ciszej/wycisz)
/// poprzez symulację klawiszy multimedialnych.
/// </summary>
public sealed class ComputerSettingsTool : IJarvisTool
{
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const uint KeyUp = 0x0002;

    public string Name => "computer_settings";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Execute, Name,
            "Steruje ustawieniami systemu — obecnie głośność: 'volume_up', 'volume_down', 'mute'. " +
            "Dla volume_up/volume_down można podać liczbę kroków.");

    [Description("Zmienia ustawienia systemu (głośność).")]
    internal string Execute(
        [Description("Akcja: volume_up, volume_down lub mute.")] string action,
        [Description("Liczba kroków zmiany głośności (dla volume_up/volume_down).")] int steps = 2)
    {
        var key = ResolveKey(action);
        if (key is null)
            return $"Nieobsługiwana akcja: {action}.";

        var repeats = key == VkVolumeMute ? 1 : Math.Clamp(steps, 1, 50);
        for (var i = 0; i < repeats; i++)
        {
            keybd_event(key.Value, 0, 0, UIntPtr.Zero);
            keybd_event(key.Value, 0, KeyUp, UIntPtr.Zero);
        }

        return action.Trim().ToLowerInvariant() switch
        {
            "mute" => "Przełączono wyciszenie.",
            "volume_up" => "Zwiększono głośność.",
            _ => "Zmniejszono głośność.",
        };
    }

    internal static byte? ResolveKey(string action) => action?.Trim().ToLowerInvariant() switch
    {
        "mute" or "wycisz" => VkVolumeMute,
        "volume_up" or "glosniej" or "głośniej" => VkVolumeUp,
        "volume_down" or "ciszej" => VkVolumeDown,
        _ => null,
    };

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
