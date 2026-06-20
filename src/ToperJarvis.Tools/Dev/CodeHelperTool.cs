using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.Dev;

/// <summary>
/// Narzędzie <c>code_helper</c> — generuje, edytuje, objaśnia, uruchamia, optymalizuje i „buduje"
/// (iteracyjny zapis→uruchom→popraw) kod w jednym pliku, korzystając z LLM. Port jednoplikowej
/// części <c>_Old/actions/code_helper.py</c>. Akcja <c>screen_debug</c> (wizja) oraz generator
/// wieloplikowy <c>dev_agent</c> — w osobnym kroku. UWAGA: akcje run/build wykonują wygenerowany kod.
/// </summary>
public sealed partial class CodeHelperTool : IJarvisTool
{
    private const int MaxBuildAttempts = 3;
    private const int DefaultTimeoutSeconds = 30;

    private static readonly IReadOnlyDictionary<string, string> ExtensionByLanguage =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["python"] = ".py", ["py"] = ".py",
            ["javascript"] = ".js", ["js"] = ".js",
            ["typescript"] = ".ts", ["ts"] = ".ts",
            ["html"] = ".html", ["css"] = ".css",
            ["java"] = ".java", ["cpp"] = ".cpp", ["c"] = ".c",
            ["bash"] = ".sh", ["shell"] = ".sh", ["powershell"] = ".ps1",
            ["sql"] = ".sql", ["json"] = ".json", ["rust"] = ".rs", ["go"] = ".go",
        };

    // Interpretery dla akcji run/build (prefiks polecenia przed ścieżką pliku).
    private static readonly IReadOnlyDictionary<string, string[]> InterpreterByExtension =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".py"] = ["python"],
            [".js"] = ["node"],
            [".ts"] = ["npx", "ts-node"],
            [".ps1"] = ["powershell", "-File"],
            [".sh"] = ["bash"],
            [".rb"] = ["ruby"],
            [".php"] = ["php"],
        };

    private static readonly string[] ErrorSignals =
        ["error", "exception", "traceback", "syntaxerror", "nameerror", "typeerror", "stderr", "failed", "crash"];

    private readonly IChatClient _chat;
    private readonly ILogger<CodeHelperTool> _logger;

    public CodeHelperTool(IChatClient chat, ILogger<CodeHelperTool> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public string Name => "code_helper";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(ExecuteAsync, Name,
            "Pomaga z kodem: write (generuj), edit (zmień plik), explain (wyjaśnij), run (uruchom), " +
            "optimize (optymalizuj), build (generuj→uruchom→popraw iteracyjnie), auto (wykryj zamiar). " +
            "UWAGA: run/build wykonują kod.");

    [Description("Generuje/edytuje/uruchamia/optymalizuje kod.")]
    private Task<string> ExecuteAsync(
        [Description("Akcja: auto, write, edit, explain, run, optimize, build.")] string action,
        [Description("Opis zadania / co napisać lub zbudować.")] string? description = null,
        [Description("Ścieżka pliku (dla edit/explain/run/optimize).")] string? filePath = null,
        [Description("Kod źródłowy (dla explain/optimize, gdy bez pliku).")] string? code = null,
        [Description("Język programowania (domyślnie python).")] string language = "python",
        [Description("Ścieżka zapisu wyniku (dla write/optimize).")] string? outputPath = null,
        [Description("Instrukcja zmiany (dla edit).")] string? instruction = null,
        [Description("Limit czasu uruchomienia w sekundach.")] int timeout = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        var resolved = string.Equals(action?.Trim(), "auto", StringComparison.OrdinalIgnoreCase)
            ? DetectIntent(description, filePath, code)
            : action?.Trim().ToLowerInvariant() ?? "";

        return resolved switch
        {
            "write" => WriteActionAsync(description, language, outputPath, cancellationToken),
            "edit" => EditActionAsync(filePath, instruction ?? description, cancellationToken),
            "explain" => ExplainActionAsync(filePath, code, cancellationToken),
            "run" => RunActionAsync(filePath, timeout, cancellationToken),
            "optimize" => OptimizeActionAsync(filePath, code, language, outputPath, cancellationToken),
            "build" => BuildActionAsync(description, language, outputPath, timeout, cancellationToken),
            _ => Task.FromResult($"Nieobsługiwana akcja: {resolved}."),
        };
    }

    private async Task<string> WriteActionAsync(string? description, string language, string? outputPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Opisz, co mam napisać.";

        var (code, path) = await GenerateAndSaveAsync(description, language, outputPath, ct);
        return $"Kod zapisany: {path}\n\nPodgląd:\n{Preview(code)}";
    }

    private async Task<string> EditActionAsync(string? filePath, string? instruction, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Podaj ścieżkę pliku do edycji.";
        if (string.IsNullOrWhiteSpace(instruction))
            return "Opisz, jaką zmianę wprowadzić.";
        if (!File.Exists(filePath))
            return $"Nie znaleziono pliku: {filePath}";

        var content = await File.ReadAllTextAsync(filePath, ct);
        const string system = "Jesteś ekspertem edycji kodu. Zwróć WYŁĄCZNIE kompletny zaktualizowany kod — bez wyjaśnień, bez markdown, bez backticków.";
        var prompt = $"Zastosuj poniższą zmianę do kodu.\n\nZmiana: {instruction}\n\nKod:\n{content}\n\nZaktualizowany kod:";

        var edited = CleanCode(await AskLlmAsync(system, prompt, ct));
        await File.WriteAllTextAsync(filePath, edited, ct);
        return $"Plik zedytowany: {filePath}\n\nPodgląd:\n{Preview(edited)}";
    }

    private async Task<string> ExplainActionAsync(string? filePath, string? code, CancellationToken ct)
    {
        code = await ResolveCodeAsync(filePath, code, ct);
        if (string.IsNullOrWhiteSpace(code))
            return "Podaj kod lub ścieżkę pliku do wyjaśnienia.";

        const string system = "Jesteś ekspertem programistą. Wyjaśnij kod zwięźle w 3-6 zdaniach po polsku.";
        var prompt = $"Wyjaśnij, co robi ten kod, jak działa i jakie są istotne szczegóły.\n\nKod:\n{Truncate(code, 4000)}\n\nWyjaśnienie:";
        return await AskLlmAsync(system, prompt, ct);
    }

    private async Task<string> RunActionAsync(string? filePath, int timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Podaj ścieżkę pliku do uruchomienia.";
        if (!File.Exists(filePath))
            return $"Nie znaleziono pliku: {filePath}";

        return await RunFileAsync(filePath, timeout, ct);
    }

    private async Task<string> OptimizeActionAsync(string? filePath, string? code, string language, string? outputPath, CancellationToken ct)
    {
        code = await ResolveCodeAsync(filePath, code, ct);
        if (string.IsNullOrWhiteSpace(code))
            return "Podaj kod lub ścieżkę pliku do optymalizacji.";

        var lang = string.IsNullOrWhiteSpace(language) ? "python" : language;
        var system = $"Jesteś ekspertem {lang}. Zwróć WYŁĄCZNIE zoptymalizowany kod — bez wyjaśnień, bez markdown, bez backticków.";
        var prompt = $"Zoptymalizuj ten kod {lang} pod kątem wydajności, czytelności i dobrych praktyk. " +
                     $"Usuń martwy kod i zbędną złożoność.\n\nKod:\n{Truncate(code, 6000)}\n\nZoptymalizowany kod:";

        var optimized = CleanCode(await AskLlmAsync(system, prompt, ct));
        var savePath = !string.IsNullOrWhiteSpace(filePath) ? filePath : ResolveSavePath(outputPath, lang);
        await SaveAsync(savePath, optimized, ct);

        var before = code.Split('\n').Length;
        var after = optimized.Split('\n').Length;
        var diff = before - after;
        return $"Kod zoptymalizowany: {savePath}\nLinie: {before} → {after} ({(diff >= 0 ? "−" : "+")}{Math.Abs(diff)})\n\nPodgląd:\n{Preview(optimized)}";
    }

    private async Task<string> BuildActionAsync(string? description, string language, string? outputPath, int timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Opisz, co mam zbudować.";

        var lang = string.IsNullOrWhiteSpace(language) ? "python" : language;
        var (code, path) = await GenerateAndSaveAsync(description, lang, outputPath, ct);

        var lastOutput = "";
        for (var attempt = 1; attempt <= MaxBuildAttempts; attempt++)
        {
            _logger.LogInformation("code_helper build: próba {Attempt}/{Max}.", attempt, MaxBuildAttempts);
            lastOutput = await RunFileAsync(path, timeout, ct);

            if (!HasError(lastOutput))
                return $"Gotowe po {attempt} próbie/-ach. Zapisano: {path}\n\nWynik:\n{lastOutput}";

            const string system = "Jesteś ekspertem debugowania. Zwróć WYŁĄCZNIE poprawiony kod — bez wyjaśnień, bez markdown, bez backticków.";
            var prompt = $"Popraw poniższy kod — zakończył się błędem.\n\nCel: {description}\n\n" +
                         $"Błąd:\n{Truncate(lastOutput, 2000)}\n\nWadliwy kod:\n{code}\n\nPoprawiony kod:";
            code = CleanCode(await AskLlmAsync(system, prompt, ct));
            await SaveAsync(path, code, ct);
        }

        return $"Nie udało się uzyskać działającej wersji po {MaxBuildAttempts} próbach. " +
               $"Ostatni błąd: {Truncate(lastOutput, 200)}\n\nOstatni kod zapisany: {path}";
    }

    private async Task<(string Code, string Path)> GenerateAndSaveAsync(string description, string language, string? outputPath, CancellationToken ct)
    {
        var lang = string.IsNullOrWhiteSpace(language) ? "python" : language;
        var system = $"Jesteś ekspertem {lang}. Zwróć WYŁĄCZNIE surowy kod — bez markdown, bez backticków, bez wyjaśnień.";
        var prompt = $"Napisz czysty, działający, dobrze skomentowany kod {lang}. " +
                     $"Obsłuż błędy i przypadki brzegowe. Stosuj nowoczesne dobre praktyki.\n\nOpis: {description}\n\nKod:";

        var code = CleanCode(await AskLlmAsync(system, prompt, ct));
        var path = ResolveSavePath(outputPath, lang);
        await SaveAsync(path, code, ct);
        return (code, path);
    }

    private async Task<string> AskLlmAsync(string system, string user, CancellationToken ct)
    {
        var response = await _chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user)],
            cancellationToken: ct);
        return response.Text;
    }

    private async Task<string> RunFileAsync(string filePath, int timeoutSeconds, CancellationToken ct)
    {
        var ext = Path.GetExtension(filePath);
        if (!InterpreterByExtension.TryGetValue(ext, out var interpreter))
            return $"Brak interpretera dla rozszerzenia {ext}.";

        var psi = new ProcessStartInfo
        {
            FileName = interpreter[0],
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var part in interpreter.Skip(1))
            psi.ArgumentList.Add(part);
        psi.ArgumentList.Add(filePath);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return "Nie udało się uruchomić procesu.";

            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* już zakończony */ }
                return $"Przekroczono limit czasu ({timeoutSeconds}s).";
            }

            var output = (await stdout).Trim();
            var error = (await stderr).Trim();
            var parts = new List<string>();
            if (output.Length > 0) parts.Add($"Wynik:\n{output}");
            if (error.Length > 0) parts.Add($"Stderr:\n{error}");
            return parts.Count > 0 ? string.Join("\n\n", parts) : "Wykonano bez wyjścia.";
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return $"Nie znaleziono interpretera: {interpreter[0]}.";
        }
        catch (Exception ex)
        {
            return $"Błąd wykonania: {ex.Message}";
        }
    }

    private async Task<string?> ResolveCodeAsync(string? filePath, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return await File.ReadAllTextAsync(filePath, ct);
        return code;
    }

    private async Task SaveAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(path, content, ct);
    }

    /// <summary>Usuwa ogradzające bloki markdown (```lang ... ```) z odpowiedzi LLM.</summary>
    internal static string CleanCode(string text)
    {
        text = text.Trim();
        text = FenceStart().Replace(text, "");
        text = FenceEnd().Replace(text, "");
        return text.Trim();
    }

    /// <summary>Ścieżka zapisu: absolutna bez zmian; względna/pusta → pulpit (domyślna nazwa wg języka).</summary>
    internal static string ResolveSavePath(string? outputPath, string language)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(outputPath))
            return Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(desktop, outputPath);

        var ext = ExtensionByLanguage.TryGetValue(language ?? "python", out var e) ? e : ".py";
        return Path.Combine(desktop, $"jarvis_code{ext}");
    }

    /// <summary>Wykrywa zamiar (akcja „auto") z opisu i obecności pliku/kodu.</summary>
    internal static string DetectIntent(string? description, string? filePath, string? code)
    {
        var desc = (description ?? "").ToLowerInvariant();
        bool Has(params string[] kws) => kws.Any(k => desc.Contains(k, StringComparison.Ordinal));

        if (Has("optimize", "refactor", "optymalizuj", "ulepsz", "uprość") && (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(filePath)))
            return "optimize";

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var exists = File.Exists(filePath);
            if (exists && Has("edit", "update", "modify", "change", "fix", "edytuj", "zmień", "popraw", "dodaj", "usuń"))
                return "edit";
            if (exists && Has("run", "execute", "launch", "uruchom", "odpal"))
                return "run";
            if (Has("build", "make it work", "zbuduj", "spraw by"))
                return "build";
            if (exists)
                return "explain";
        }

        if (Has("explain", "what does", "describe", "analyze", "wyjaśnij", "wytłumacz", "opisz", "co robi") &&
            (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(filePath)))
            return "explain";

        if (Has("build", "make it work", "zbuduj", "spraw by"))
            return "build";

        return "write";
    }

    /// <summary>Czy wyjście uruchomienia wygląda na błąd (do pętli auto-fix).</summary>
    internal static bool HasError(string output)
    {
        var lower = output.ToLowerInvariant();
        return ErrorSignals.Any(s => lower.Contains(s, StringComparison.Ordinal));
    }

    private static string Preview(string code, int lines = 10)
    {
        var all = code.Split('\n');
        var head = string.Join("\n", all.Take(lines));
        return all.Length > lines ? $"{head}\n... (jeszcze {all.Length - lines} linii)" : head;
    }

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;

    [GeneratedRegex(@"^```[a-zA-Z]*\n?")]
    private static partial Regex FenceStart();

    [GeneratedRegex(@"\n?```$")]
    private static partial Regex FenceEnd();
}
