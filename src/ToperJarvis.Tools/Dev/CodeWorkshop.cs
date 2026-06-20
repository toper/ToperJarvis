using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ToperJarvis.Tools.Dev;

/// <summary>
/// Wspólne helpery narzędzi deweloperskich (code_helper, dev_agent): czyszczenie odpowiedzi LLM
/// z płotków markdown, wywołanie LLM oraz uruchamianie procesu z limitem czasu i zabiciem drzewa procesów.
/// </summary>
internal static partial class CodeWorkshop
{
    /// <summary>Prefiks komunikatu o przekroczeniu limitu czasu (wspólny — unika sprzężenia po stałym tekście).</summary>
    public const string TimeoutPrefix = "Przekroczono limit czasu";

    /// <summary>Usuwa ogradzające bloki markdown (```lang ... ```) z odpowiedzi LLM.</summary>
    public static string CleanCode(string? text)
    {
        text = (text ?? "").Trim();
        text = FenceStart().Replace(text, "");
        text = FenceEnd().Replace(text, "");
        return text.Trim();
    }

    /// <summary>Wywołuje LLM z wiadomością systemową i użytkownika; zwraca tekst odpowiedzi.</summary>
    public static async Task<string> AskLlmAsync(IChatClient chat, string system, string user, CancellationToken ct)
    {
        var response = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user)],
            cancellationToken: ct);
        return response.Text;
    }

    /// <summary>
    /// Uruchamia proces, zwraca połączone stdout/stderr oraz czy zakończono sukcesem (exit code 0).
    /// Limit czasu zabija drzewo procesów. Brak pliku wykonywalnego → (komunikat, false).
    /// </summary>
    public static async Task<(string Output, bool Ok)> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        int timeoutSeconds,
        ILogger logger,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return ("Nie udało się uruchomić procesu.", false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* już zakończony */ }
                return ($"{TimeoutPrefix} ({timeoutSeconds}s).", false);
            }

            var output = (await stdout).Trim();
            var error = (await stderr).Trim();
            var parts = new List<string>();
            if (output.Length > 0) parts.Add($"Wynik:\n{output}");
            if (error.Length > 0) parts.Add($"Stderr:\n{error}");
            var text = parts.Count > 0 ? string.Join("\n\n", parts) : "Wykonano bez wyjścia.";
            return (text, process.ExitCode == 0);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            logger.LogWarning(ex, "Nie znaleziono pliku wykonywalnego: {File}.", fileName);
            return ($"Nie znaleziono polecenia: {fileName}.", false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Błąd uruchomienia procesu {File}.", fileName);
            return ($"Błąd wykonania: {ex.Message}", false);
        }
    }

    [GeneratedRegex(@"^```[a-zA-Z]*\n?")]
    private static partial Regex FenceStart();

    [GeneratedRegex(@"\n?```$")]
    private static partial Regex FenceEnd();
}
