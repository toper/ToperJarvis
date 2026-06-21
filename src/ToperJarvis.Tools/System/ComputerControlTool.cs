using System.ComponentModel;
using System.Threading;
using Microsoft.Extensions.AI;
using ToperJarvis.Abstractions.Tools;
using ToperJarvis.Tools.Input;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>computer_control</c> — bezpośrednie sterowanie myszą i klawiaturą: pisanie tekstu,
/// pojedyncze klawisze, skróty, kliknięcia, ruch kursora i przewijanie.
/// </summary>
public sealed class ComputerControlTool : IJarvisTool
{
    public string Name => "computer_control";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Execute, Name,
            "Steruje myszą i klawiaturą: type (pisze tekst), press (klawisz), hotkey (skrót np. ctrl+c), " +
            "click, right_click, move (x,y), scroll (amount), focus (aktywuje okno po fragmencie tytułu). " +
            "Aby wpisać do konkretnego okna, podaj 'window' przy akcji type — najpierw aktywuje to okno.");

    [Description("Steruje myszą/klawiaturą.")]
    private string Execute(
        [Description("Akcja: type, press, hotkey, click, right_click, move, scroll, focus.")] string action,
        [Description("Tekst do wpisania (type), nazwa klawisza/skrótu (press/hotkey) lub fragment tytułu okna (focus).")]
        string? text = null,
        [Description("Fragment tytułu okna do aktywacji PRZED wpisaniem (dla type). Opcjonalne.")]
        string? window = null,
        [Description("Współrzędna X (dla move).")] int x = 0,
        [Description("Współrzędna Y (dla move).")] int y = 0,
        [Description("Wartość przewinięcia (dla scroll; +góra/-dół).")] int amount = 0)
    {
        switch (action?.Trim().ToLowerInvariant())
        {
            case "focus":
                if (string.IsNullOrWhiteSpace(text))
                    return "Podaj fragment tytułu okna do aktywacji.";
                return InputSimulator.FocusWindow(text)
                    ? $"Aktywowano okno: {text}."
                    : $"Nie znaleziono okna zawierającego: {text}.";

            case "type":
                if (string.IsNullOrEmpty(text))
                    return "Brak tekstu do wpisania.";
                if (!string.IsNullOrWhiteSpace(window) && !InputSimulator.FocusWindow(window))
                    return $"Nie znaleziono okna '{window}' — nie wpisuję, by nie trafić w złe okno.";
                if (!string.IsNullOrWhiteSpace(window))
                    Thread.Sleep(150); // chwila na przejęcie fokusu przez okno
                var sent = InputSimulator.TypeText(text);
                return sent == 0
                    ? "Nie udało się wpisać tekstu (system zablokował wejście — okno docelowe może działać jako administrator)."
                    : $"Wpisano tekst do aktywnego okna ({text.Length} znaków).";

            case "press":
                var vk = KeyMap.ParseKey(text ?? "");
                if (vk is null)
                    return $"Nieznany klawisz: {text}.";
                InputSimulator.PressKey(vk.Value);
                return $"Wciśnięto: {text}.";

            case "hotkey":
                var combo = KeyMap.ParseHotkey(text ?? "");
                if (combo.Count == 0)
                    return $"Nieprawidłowy skrót: {text}.";
                InputSimulator.Hotkey(combo);
                return $"Wykonano skrót: {text}.";

            case "click":
                InputSimulator.Click();
                return "Kliknięto.";

            case "right_click":
                InputSimulator.Click(right: true);
                return "Kliknięto prawym przyciskiem.";

            case "move":
                InputSimulator.MoveMouse(x, y);
                return $"Przesunięto kursor do ({x}, {y}).";

            case "scroll":
                InputSimulator.Scroll(amount);
                return "Przewinięto.";

            default:
                return $"Nieobsługiwana akcja: {action}.";
        }
    }
}
