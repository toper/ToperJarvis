namespace ToperJarvis.Tools.Input;

/// <summary>Mapuje nazwy klawiszy na wirtualne kody (VK) oraz parsuje skróty typu „ctrl+shift+s".</summary>
internal static class KeyMap
{
    private static readonly Dictionary<string, byte> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["tab"] = 0x09,
        ["space"] = 0x20, ["spacja"] = 0x20,
        ["backspace"] = 0x08,
        ["delete"] = 0x2E, ["del"] = 0x2E,
        ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["ctrl"] = 0x11, ["control"] = 0x11,
        ["alt"] = 0x12,
        ["shift"] = 0x10,
        ["win"] = 0x5B, ["windows"] = 0x5B,
    };

    /// <summary>Zwraca VK dla nazwy klawisza (litery/cyfry/f1-f12/nazwane). Null jeśli nieznany.</summary>
    public static byte? ParseKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var k = key.Trim();

        if (Keys.TryGetValue(k, out var vk))
            return vk;

        if (k.Length == 1)
        {
            var c = char.ToUpperInvariant(k[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return (byte)c;
        }

        if ((k[0] is 'f' or 'F') && int.TryParse(k.AsSpan(1), out var n) && n is >= 1 and <= 12)
            return (byte)(0x70 + (n - 1)); // VK_F1 = 0x70

        return null;
    }

    /// <summary>Parsuje skrót („ctrl+shift+s") na listę VK w kolejności wciskania. Pusta jeśli błędny.</summary>
    public static IReadOnlyList<byte> ParseHotkey(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo))
            return Array.Empty<byte>();

        var result = new List<byte>();
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var vk = ParseKey(part);
            if (vk is null)
                return Array.Empty<byte>(); // nieznany składnik → cały skrót nieprawidłowy
            result.Add(vk.Value);
        }

        return result;
    }
}
