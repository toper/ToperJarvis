using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>reminder</c> — ustawia przypomnienie poprzez Harmonogram zadań Windows
/// (<c>schtasks</c>). O wskazanej porze wyświetla okno z treścią przypomnienia.
/// </summary>
public sealed class ReminderTool : IJarvisTool
{
    private readonly ILogger<ReminderTool> _logger;

    public ReminderTool(ILogger<ReminderTool> logger) => _logger = logger;

    public string Name => "reminder";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Create, Name,
            "Ustawia przypomnienie na konkretną datę i godzinę (wyświetli okno z treścią).");

    [Description("Ustawia przypomnienie.")]
    private string Create(
        [Description("Data w formacie RRRR-MM-DD.")] string date,
        [Description("Godzina w formacie GG:MM (24h).")] string time,
        [Description("Treść przypomnienia.")] string message)
    {
        if (!TryParseDue(date, time, out var due))
            return "Niepoprawna data lub godzina. Użyj formatu RRRR-MM-DD i GG:MM.";

        if (due <= DateTime.Now)
            return "Podany termin już minął.";

        if (string.IsNullOrWhiteSpace(message))
            message = "Przypomnienie";

        // /tr musi być jednowierszowe — normalizujemy ewentualne znaki nowej linii z wejścia LLM.
        message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();

        var taskName = "Jarvis_Reminder_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var psi = new ProcessStartInfo("schtasks")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/create");
            psi.ArgumentList.Add("/tn");
            psi.ArgumentList.Add(taskName);
            psi.ArgumentList.Add("/sc");
            psi.ArgumentList.Add("once");
            psi.ArgumentList.Add("/st");
            psi.ArgumentList.Add(due.ToString("HH:mm", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("/sd");
            psi.ArgumentList.Add(due.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern,
                CultureInfo.CurrentCulture));
            psi.ArgumentList.Add("/tr");
            psi.ArgumentList.Add(BuildPopupCommand(message));
            psi.ArgumentList.Add("/it"); // interaktywne — okno widoczne w sesji użytkownika
            psi.ArgumentList.Add("/z");  // usuń zadanie po jednorazowym wykonaniu (brak kumulacji)
            psi.ArgumentList.Add("/f");

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("schtasks zwrócił {Code}: {Err}", process.ExitCode, stderr);
                return "Nie udało się ustawić przypomnienia.";
            }

            return $"Ustawiono przypomnienie na {due:yyyy-MM-dd HH:mm}: {message}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd ustawiania przypomnienia.");
            return "Nie udało się ustawić przypomnienia.";
        }
    }

    /// <summary>
    /// Waliduje wyłącznie FORMAT daty (RRRR-MM-DD) i godziny (GG:MM) i zwraca <see cref="DateTime"/>.
    /// Sprawdzenie, czy termin nie jest w przeszłości, należy do <c>Create</c>.
    /// </summary>
    internal static bool TryParseDue(string date, string time, out DateTime due)
    {
        due = default;
        return DateTime.TryParseExact(
            $"{date?.Trim()} {time?.Trim()}",
            "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out due);
    }

    /// <summary>Buduje polecenie pokazujące okno przypomnienia (PowerShell MessageBox).</summary>
    private static string BuildPopupCommand(string message)
    {
        // Apostrofy w treści podwajamy dla literału PowerShell.
        var safe = message.Replace("'", "''");
        return "powershell -WindowStyle Hidden -Command " +
               $"\"Add-Type -AssemblyName PresentationFramework; " +
               $"[System.Windows.MessageBox]::Show('{safe}','Jarvis - przypomnienie')\"";
    }
}
