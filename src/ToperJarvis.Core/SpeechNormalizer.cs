using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ToperJarvis.Core;

/// <summary>
/// Normalizuje tekst PRZED syntezą TTS: usuwa formatowanie Markdown (Piper czytałby na głos znaki
/// jak „gwiazdki") i stosuje leksykon wymowy (skróty/symbole, np. „USD" → „dolarów"). Zestaw domyślny
/// (PL) jest dopełniany/nadpisywany słownikiem z konfiguracji (<c>Tts:Lexicon</c>).
/// </summary>
public static class SpeechNormalizer
{
    // Domyślny leksykon PL. Klucze będące „słowami" (litery/cyfry) podmieniamy z granicą słowa
    // i bez względu na wielkość liter; symbole (%, $, °C) — dosłownie.
    private static readonly Dictionary<string, string> DefaultLexicon = new(StringComparer.Ordinal)
    {
        ["°C"] = " stopni Celsjusza",
        ["°F"] = " stopni Fahrenheita",
        ["%"] = " procent",
        ["$"] = " dolarów",
        ["€"] = " euro",
        ["£"] = " funtów",
        ["&"] = " i ",
        ["USD"] = "dolarów",
        ["EUR"] = "euro",
        ["PLN"] = "złotych",
        ["GBP"] = "funtów",
        ["CHF"] = "franków",
        ["kWh"] = "kilowatogodzin",
        ["kg"] = "kilogramów",
        ["km"] = "kilometrów",
        ["cm"] = "centymetrów",
        ["mm"] = "milimetrów",
        ["MB"] = "megabajtów",
        ["GB"] = "gigabajtów",
        ["TB"] = "terabajtów",
        ["r/r"] = "rok do roku",
        ["np"] = "na przykład",
        ["itp"] = "i tym podobne",
        ["itd"] = "i tak dalej",
        ["tzn"] = "to znaczy",
    };

    /// <summary>Zwraca tekst gotowy do TTS: bez Markdown, z zastosowanym leksykonem.</summary>
    public static string Normalize(string text, IReadOnlyDictionary<string, string>? extraLexicon = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = StripMarkdown(text);
        text = ApplyLexicon(text, extraLexicon);

        // Złączenie wielokrotnych spacji i przycięcie.
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        return text.Trim();
    }

    private static string StripMarkdown(string text)
    {
        // Bloki i fragmenty kodu — usuń ograniczniki (treść czytamy normalnie).
        text = Regex.Replace(text, "```[a-zA-Z0-9]*", " ");
        text = text.Replace("`", "");
        // Obrazki i linki: zostaw tekst/alt, wytnij URL.
        text = Regex.Replace(text, @"!\[([^\]]*)\]\([^)]*\)", "$1");
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
        // Nagłówki, cytaty, punktory na początku linii.
        text = Regex.Replace(text, @"(?m)^\s{0,3}#{1,6}\s*", "");
        text = Regex.Replace(text, @"(?m)^\s{0,3}>\s?", "");
        text = Regex.Replace(text, @"(?m)^\s*[-*+]\s+", "");
        // Pozostałe znaki nacisku (bold/italic/strike) i podkreślenia.
        text = Regex.Replace(text, @"[*_~]", "");
        return text;
    }

    private static string ApplyLexicon(string text, IReadOnlyDictionary<string, string>? extra)
    {
        // Domyślny zestaw + konfiguracja (config nadpisuje przy kolizji klucza).
        var merged = new Dictionary<string, string>(DefaultLexicon, StringComparer.Ordinal);
        if (extra is not null)
            foreach (var (k, v) in extra)
                merged[k] = v;

        // Dłuższe klucze najpierw (np. „°C" przed „°", „USD" przed potencjalnym „US").
        foreach (var key in merged.Keys.OrderByDescending(k => k.Length))
        {
            var value = merged[key];
            if (IsWordKey(key))
                text = Regex.Replace(text, $@"\b{Regex.Escape(key)}\b", _ => value, RegexOptions.IgnoreCase);
            else
                text = text.Replace(key, value);
        }

        return text;
    }

    private static bool IsWordKey(string key) => key.All(c => char.IsLetterOrDigit(c) || c == '_');
}
