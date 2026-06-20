using System.ComponentModel;
using System.Diagnostics;
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

        try
        {
            var (code, path) = await GenerateAndSaveAsync(description, language, outputPath, ct);
            return $"Kod zapisany: {path}\n\nPodgląd:\n{Preview(code)}";
        }
        catch (CodeGenerationException ex)
        {
            return ex.Message;
        }
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

        try
        {
            // Walidacja niepustego wyniku PRZED zapisem — nie nadpisujemy działającego pliku pustką.
            var edited = await GenerateCodeAsync(system, prompt, ct);
            await SaveAsync(filePath, edited, ct);
            return $"Plik zedytowany: {filePath}\n\nPodgląd:\n{Preview(edited)}";
        }
        catch (CodeGenerationException ex)
        {
            return $"{ex.Message} Plik nietknięty.";
        }
    }

    private async Task<string> ExplainActionAsync(string? filePath, string? code, CancellationToken ct)
    {
        code = await ResolveCodeAsync(filePath, code, ct);
        if (string.IsNullOrWhiteSpace(code))
            return "Podaj kod lub ścieżkę pliku do wyjaśnienia.";

        const string system = "Jesteś ekspertem programistą. Wyjaśnij kod zwięźle w 3-6 zdaniach po polsku.";
        var prompt = $"Wyjaśnij, co robi ten kod, jak działa i jakie są istotne szczegóły.\n\nKod:\n{Truncate(code, 4000)}\n\nWyjaśnienie:";

        try
        {
            return await AskLlmAsync(system, prompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd wyjaśniania kodu.");
            return $"Nie udało się wyjaśnić kodu: {ex.Message}";
        }
    }

    private async Task<string> RunActionAsync(string? filePath, int timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Podaj ścieżkę pliku do uruchomienia.";
        if (!File.Exists(filePath))
            return $"Nie znaleziono pliku: {filePath}";

        var (output, _) = await RunFileAsync(filePath, timeout, ct);
        return output;
    }

    private async Task<string> OptimizeActionAsync(string? filePath, string? code, string language, string? outputPath, CancellationToken ct)
    {
        code = await ResolveCodeAsync(filePath, code, ct);
        if (string.IsNullOrWhiteSpace(code))
            return "Podaj kod lub ścieżkę pliku do optymalizacji.";

        var lang = NormalizeLanguage(language);
        var system = $"Jesteś ekspertem {lang}. Zwróć WYŁĄCZNIE zoptymalizowany kod — bez wyjaśnień, bez markdown, bez backticków.";
        var prompt = $"Zoptymalizuj ten kod {lang} pod kątem wydajności, czytelności i dobrych praktyk. " +
                     $"Usuń martwy kod i zbędną złożoność.\n\nKod:\n{Truncate(code, 6000)}\n\nZoptymalizowany kod:";

        string optimized;
        try
        {
            optimized = await GenerateCodeAsync(system, prompt, ct);
        }
        catch (CodeGenerationException ex)
        {
            return $"{ex.Message} Plik nietknięty.";
        }

        var savePath = !string.IsNullOrWhiteSpace(filePath) ? filePath : ResolveSavePath(outputPath, lang);
        await SaveAsync(savePath, optimized, ct);

        var before = CountLines(code);
        var after = CountLines(optimized);
        var diff = before - after;
        return $"Kod zoptymalizowany: {savePath}\nLinie: {before} → {after} ({(diff >= 0 ? "−" : "+")}{Math.Abs(diff)})\n\nPodgląd:\n{Preview(optimized)}";
    }

    private async Task<string> BuildActionAsync(string? description, string language, string? outputPath, int timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Opisz, co mam zbudować.";

        string code, path;
        try
        {
            (code, path) = await GenerateAndSaveAsync(description, language, outputPath, ct);
        }
        catch (CodeGenerationException ex)
        {
            return ex.Message;
        }

        var lastOutput = "";
        for (var attempt = 1; attempt <= MaxBuildAttempts; attempt++)
        {
            _logger.LogInformation("code_helper build: próba {Attempt}/{Max}.", attempt, MaxBuildAttempts);
            bool ok;
            (lastOutput, ok) = await RunFileAsync(path, timeout, ct);

            if (ok)
                return $"Gotowe po {attempt} próbie/-ach. Zapisano: {path}\n\nWynik:\n{lastOutput}";

            const string system = "Jesteś ekspertem debugowania. Zwróć WYŁĄCZNIE poprawiony kod — bez wyjaśnień, bez markdown, bez backticków.";
            var prompt = $"Popraw poniższy kod — zakończył się błędem.\n\nCel: {description}\n\n" +
                         $"Błąd:\n{Truncate(lastOutput, 2000)}\n\nWadliwy kod:\n{code}\n\nPoprawiony kod:";
            try
            {
                code = await GenerateCodeAsync(system, prompt, ct);
                await SaveAsync(path, code, ct);
            }
            catch (CodeGenerationException ex)
            {
                return $"{ex.Message} Ostatni działający zapis: {path}";
            }
        }

        return $"Nie udało się uzyskać działającej wersji po {MaxBuildAttempts} próbach. " +
               $"Ostatni błąd: {Truncate(lastOutput, 200)}\n\nOstatni kod zapisany: {path}";
    }

    private async Task<(string Code, string Path)> GenerateAndSaveAsync(string description, string language, string? outputPath, CancellationToken ct)
    {
        var lang = NormalizeLanguage(language);
        var system = $"Jesteś ekspertem {lang}. Zwróć WYŁĄCZNIE surowy kod — bez markdown, bez backticków, bez wyjaśnień.";
        var prompt = $"Napisz czysty, działający, dobrze skomentowany kod {lang}. " +
                     $"Obsłuż błędy i przypadki brzegowe. Stosuj nowoczesne dobre praktyki.\n\nOpis: {description}\n\nKod:";

        var code = await GenerateCodeAsync(system, prompt, ct);
        var path = ResolveSavePath(outputPath, lang);
        await SaveAsync(path, code, ct);
        return (code, path);
    }

    /// <summary>Pyta LLM o kod, czyści płotki i waliduje niepustość. Rzuca <see cref="CodeGenerationException"/> przy błędzie/pustce.</summary>
    private async Task<string> GenerateCodeAsync(string system, string prompt, CancellationToken ct)
    {
        string raw;
        try
        {
            raw = await AskLlmAsync(system, prompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd wywołania LLM (generowanie kodu).");
            throw new CodeGenerationException("LLM nie odpowiedział — kod niewygenerowany.");
        }

        var clean = CleanCode(raw);
        if (string.IsNullOrWhiteSpace(clean))
            throw new CodeGenerationException("LLM nie zwrócił kodu.");
        return clean;
    }

    private async Task<string> AskLlmAsync(string system, string user, CancellationToken ct)
    {
        var response = await _chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user)],
            cancellationToken: ct);
        return response.Text;
    }

    /// <summary>Uruchamia plik właściwym interpreterem. Zwraca wyjście i czy zakończono sukcesem (exit code 0).</summary>
    private async Task<(string Output, bool Ok)> RunFileAsync(string filePath, int timeoutSeconds, CancellationToken ct)
    {
        var ext = Path.GetExtension(filePath);
        if (!InterpreterByExtension.TryGetValue(ext, out var interpreter))
            return ($"Brak interpretera dla rozszerzenia {ext}.", false);

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
                return ("Nie udało się uruchomić procesu.", false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            // Czytamy strumienie z tym samym tokenem co WaitForExit, by timeout zwijał też odczyt.
            var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* już zakończony */ }
                return ($"Przekroczono limit czasu ({timeoutSeconds}s).", false);
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
            return ($"Nie znaleziono interpretera: {interpreter[0]}.", false);
        }
        catch (Exception ex)
        {
            return ($"Błąd wykonania: {ex.Message}", false);
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

    /// <summary>
    /// Ścieżka zapisu: absolutna bez zmian; względna → względem pulpitu; pusta → pulpit (nazwa wg języka).
    /// Gdy katalog pulpitu jest niedostępny (np. usługa Windows), fallback do katalogu tymczasowego.
    /// </summary>
    internal static string ResolveSavePath(string? outputPath, string language)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.GetTempPath();

        if (!string.IsNullOrWhiteSpace(outputPath))
            return Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(baseDir, outputPath);

        var ext = ExtensionByLanguage.TryGetValue(NormalizeLanguage(language), out var e) ? e : ".py";
        return Path.Combine(baseDir, $"jarvis_code{ext}");
    }

    /// <summary>Wykrywa zamiar (akcja „auto") z opisu i obecności pliku/kodu.</summary>
    internal static string DetectIntent(string? description, string? filePath, string? code)
    {
        var desc = (description ?? "").ToLowerInvariant();
        bool Has(params string[] kws) => kws.Any(k => desc.Contains(k, StringComparison.Ordinal));

        if (Has("optimize", "refactor", "clean up", "improve", "make it better", "optymalizuj", "ulepsz", "uprość") &&
            (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(filePath)))
            return "optimize";

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var exists = File.Exists(filePath);
            if (exists && Has("edit", "update", "modify", "change", "add", "remove", "rename", "replace", "fix",
                              "edytuj", "zmień", "popraw", "dodaj", "usuń"))
                return "edit";
            if (exists && Has("run", "execute", "launch", "uruchom", "odpal"))
                return "run";
            if (Has("build", "make it work", "try", "attempt", "zbuduj", "spraw by"))
                return "build";
            if (exists)
                return "explain";
        }

        if (Has("explain", "what does", "describe", "analyze", "wyjaśnij", "wytłumacz", "opisz", "co robi") &&
            (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(filePath)))
            return "explain";

        if (Has("build", "make it work", "try", "attempt", "zbuduj", "spraw by"))
            return "build";

        return "write";
    }

    private static string NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "python" : language;

    private static int CountLines(string text) => text.AsSpan().Count('\n') + 1;

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

    /// <summary>Sygnalizuje, że LLM nie dostarczył użytecznego kodu — wołający NIE zapisuje pliku.</summary>
    private sealed class CodeGenerationException(string message) : Exception(message);
}
