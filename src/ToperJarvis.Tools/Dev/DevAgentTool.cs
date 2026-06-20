using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.Dev;

/// <summary>
/// Narzędzie <c>dev_agent</c> — buduje WIELOPLIKOWY projekt: planuje strukturę (LLM→JSON), pisze
/// pliki w kolejności zależności, instaluje zależności (pip dla Pythona) i iteracyjnie uruchamia
/// →poprawia aż do działania. Port <c>_Old/actions/dev_agent.py</c>. UWAGA: instaluje pakiety i
/// wykonuje wygenerowany kod. Uzupełnia jednoplikowy [code_helper].
/// </summary>
public sealed partial class DevAgentTool : IJarvisTool
{
    private const int MaxBuildAttempts = 3;
    private const int MaxAutoInstalls = 3;
    private const int DefaultTimeoutSeconds = 30;
    private const int PipTimeoutSeconds = 120;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chat;
    private readonly ILogger<DevAgentTool> _logger;

    public DevAgentTool(IChatClient chat, ILogger<DevAgentTool> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    public string Name => "dev_agent";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(BuildProjectAsync, Name,
            "Buduje kompletny, wieloplikowy projekt z opisu: planuje strukturę, pisze pliki, " +
            "instaluje zależności i uruchamia, poprawiając błędy iteracyjnie. UWAGA: instaluje pakiety " +
            "i wykonuje kod. Dla pojedynczego pliku użyj code_helper.");

    [Description("Buduje wieloplikowy projekt z opisu.")]
    private async Task<string> BuildProjectAsync(
        [Description("Opis projektu do zbudowania.")] string description,
        [Description("Język programowania (domyślnie python).")] string language = "python",
        [Description("Nazwa projektu (opcjonalna — inaczej z planu).")] string? projectName = null,
        [Description("Limit czasu uruchomienia w sekundach.")] int timeout = DefaultTimeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Opisz projekt, który mam zbudować.";

        var lang = string.IsNullOrWhiteSpace(language) ? "python" : language;

        ProjectPlan? plan;
        try
        {
            plan = ParsePlan(await AskLlmAsync(PlannerSystem, PlannerPrompt(description, lang), cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd planowania projektu.");
            return $"Nie udało się zaplanować projektu: {ex.Message}";
        }

        if (plan is null || plan.Files.Count == 0)
            return "Planer nie zwrócił poprawnej struktury projektu.";

        var projName = SanitizeProjectName(!string.IsNullOrWhiteSpace(projectName) ? projectName! : plan.ProjectName);
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Path.GetTempPath();
        var projectDir = Path.Combine(baseDir, projName);
        Directory.CreateDirectory(projectDir);

        var entryPoint = string.IsNullOrWhiteSpace(plan.EntryPoint) ? "main.py" : plan.EntryPoint;
        var runCommand = string.IsNullOrWhiteSpace(plan.RunCommand) ? $"python {entryPoint}" : plan.RunCommand;

        // Pliki w kolejności zależności (najpierw te z najmniejszą liczbą importów wewnętrznych).
        var sortedFiles = plan.Files.Where(f => !string.IsNullOrWhiteSpace(f.Path))
                                    .OrderBy(f => f.Imports.Count).ToList();
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in sortedFiles)
        {
            try
            {
                var code = CodeWorkshop.CleanCode(
                    await AskLlmAsync(WriterSystem(lang), WriterPrompt(description, plan.Files, file, written), cancellationToken));
                await SaveFileAsync(projectDir, file.Path, code, cancellationToken);
                written[file.Path] = code;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nie udało się zapisać pliku {Path}.", file.Path);
            }
        }

        if (written.Count == 0)
            return "Nie udało się zapisać żadnego pliku projektu.";

        if (plan.Dependencies.Count > 0 && lang.Equals("python", StringComparison.OrdinalIgnoreCase))
            _logger.LogInformation("dev_agent: {Result}", await InstallPythonDependenciesAsync(plan.Dependencies, projectDir, cancellationToken));

        var lastOutput = "";
        var autoInstalls = 0;
        for (var attempt = 1; attempt <= MaxBuildAttempts; attempt++)
        {
            _logger.LogInformation("dev_agent build: próba {Attempt}/{Max}.", attempt, MaxBuildAttempts);
            bool ok;
            (lastOutput, ok) = await RunProjectAsync(runCommand, projectDir, timeout, cancellationToken);

            // Timeout traktujemy jak sukces — długo działająca aplikacja (serwer/GUI) zwykle działa.
            if (ok || lastOutput.Contains(CodeWorkshop.TimeoutPrefix, StringComparison.Ordinal))
                return $"Projekt '{projName}' działa (próba {attempt}). Zapisano: {projectDir}\n\nWynik:\n{lastOutput}";

            if (attempt == MaxBuildAttempts)
                break;

            var missing = ExtractMissingModule(lastOutput);
            if (missing is not null && autoInstalls < MaxAutoInstalls)
            {
                autoInstalls++;
                _logger.LogInformation("dev_agent: auto-instalacja brakującego pakietu {Pkg}.", missing);
                await InstallPythonDependenciesAsync([missing], projectDir, cancellationToken);
                continue;
            }

            await FixFileAsync(projectDir, entryPoint, written, description, lang, lastOutput, cancellationToken);
        }

        return $"Nie udało się uzyskać działającego projektu '{projName}' po {MaxBuildAttempts} próbach. " +
               $"Zapisano: {projectDir}\n\nOstatni błąd:\n{Truncate(lastOutput, 600)}";
    }

    private async Task FixFileAsync(string projectDir, string entryPoint, Dictionary<string, string> written,
        string description, string lang, string errorOutput, CancellationToken ct)
    {
        // v1: poprawiamy plik wskazany w błędzie (jeśli rozpoznany) lub punkt wejścia.
        var target = FindErrorFile(errorOutput, written.Keys) ?? entryPoint;
        if (!written.TryGetValue(target, out var current))
            return;

        var system = $"Jesteś ekspertem debugowania {lang}. Zwróć WYŁĄCZNIE kompletny poprawiony kod — bez wyjaśnień, bez markdown, bez backticków.";
        var prompt = $"Cel projektu: {description}\n\nPlik do naprawy: {target}\n\n" +
                     $"Błąd:\n{Truncate(errorOutput, 2500)}\n\nObecny kod:\n{current}\n\nPoprawiony kod dla {target}:";
        try
        {
            var fixedCode = CodeWorkshop.CleanCode(await AskLlmAsync(system, prompt, ct));
            if (!string.IsNullOrWhiteSpace(fixedCode))
            {
                await SaveFileAsync(projectDir, target, fixedCode, ct);
                written[target] = fixedCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się poprawić pliku {Path}.", target);
        }
    }

    private async Task<(string Output, bool Ok)> RunProjectAsync(string runCommand, string projectDir, int timeout, CancellationToken ct)
    {
        var parts = runCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return ("Brak polecenia uruchomienia.", false);

        return await CodeWorkshop.RunProcessAsync(parts[0], parts.Skip(1), projectDir, timeout, _logger, ct);
    }

    private async Task<string> InstallPythonDependenciesAsync(IReadOnlyList<string> dependencies, string projectDir, CancellationToken ct)
    {
        var args = new[] { "-m", "pip", "install" }.Concat(dependencies);
        var (output, ok) = await CodeWorkshop.RunProcessAsync("python", args, projectDir, PipTimeoutSeconds, _logger, ct);
        return ok ? $"Zainstalowano zależności: {string.Join(", ", dependencies)}." : $"Instalacja zależności (nie-fatalne): {Truncate(output, 200)}";
    }

    private async Task SaveFileAsync(string projectDir, string relativePath, string content, CancellationToken ct)
    {
        // Plan pochodzi od LLM — pilnujemy, by ścieżka nie wyszła poza katalog projektu (np. „../..").
        var root = Path.GetFullPath(projectDir);
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Ścieżka pliku wykracza poza katalog projektu: {relativePath}");

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, content, ct);
    }

    private Task<string> AskLlmAsync(string system, string user, CancellationToken ct) =>
        CodeWorkshop.AskLlmAsync(_chat, system, user, ct);

    // ── Czysta logika (testowalna) ──────────────────────────────────────────────

    /// <summary>Parsuje plan projektu z odpowiedzi LLM (JSON, ewentualnie w płotkach). Null gdy niepoprawny.</summary>
    internal static ProjectPlan? ParsePlan(string llmResponse)
    {
        try
        {
            return JsonSerializer.Deserialize<ProjectPlan>(CodeWorkshop.CleanCode(llmResponse), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sanityzuje nazwę projektu do bezpiecznej nazwy katalogu (znaki spoza [\w-] → _).</summary>
    internal static string SanitizeProjectName(string name)
    {
        var cleaned = NonNameChars().Replace(name?.Trim() ?? "", "_").Trim('_');
        return cleaned.Length > 0 ? cleaned : "jarvis_project";
    }

    /// <summary>Wyłuskuje nazwę brakującego pakietu z „ModuleNotFoundError: No module named 'x'".</summary>
    internal static string? ExtractMissingModule(string output)
    {
        var match = MissingModule().Match(output);
        if (!match.Success)
            return null;
        return match.Groups[1].Value.Replace('_', '-').Split('.')[0];
    }

    /// <summary>Znajduje pierwszy znany plik projektu wymieniony w treści błędu (np. traceback).</summary>
    internal static string? FindErrorFile(string output, IEnumerable<string> knownPaths) =>
        knownPaths.FirstOrDefault(p => output.Contains(Path.GetFileName(p), StringComparison.Ordinal));

    private static string Truncate(string text, int max) => text.Length > max ? text[..max] : text;

    private const string PlannerSystem =
        "Jesteś starszym architektem oprogramowania. Zwróć WYŁĄCZNIE poprawny JSON — bez markdown, bez wyjaśnień.";

    private static string PlannerPrompt(string description, string language) =>
        $$"""
        Stwórz minimalny, kompletny plan plików dla tego projektu.

        Język: {{language}}
        Opis: {{description}}

        Zwróć WYŁĄCZNIE poprawny JSON:
        {
          "project_name": "nazwa_snake_case",
          "entry_point": "main.py",
          "files": [
            { "path": "main.py", "description": "Punkt wejścia", "imports": ["utils.helpers"] }
          ],
          "run_command": "python main.py",
          "dependencies": ["requests"]
        }

        Zasady: kolejność zależności, ścieżki względne, biblioteka standardowa NIE w dependencies.

        JSON:
        """;

    private static string WriterSystem(string language) =>
        $"Jesteś starszym programistą {language}. Zwróć WYŁĄCZNIE surowy kod — bez wyjaśnień, bez markdown, bez backticków.";

    private static string WriterPrompt(string description, IReadOnlyList<PlanFile> allFiles, PlanFile file, IReadOnlyDictionary<string, string> written)
    {
        var fileList = string.Join("\n", allFiles.Select((f, i) => $"  [{i + 1}] {f.Path}: {f.Description}"));
        var depContext = "";
        foreach (var dep in file.Imports)
        {
            var depPath = dep.Replace('.', '/') + ".py";
            if (written.TryGetValue(depPath, out var depCode))
                depContext += $"\n\n--- {depPath} ---\n{Truncate(depCode, 2000)}";
        }

        var importsLine = file.Imports.Count > 0
            ? "Importuje z: " + string.Join(", ", file.Imports)
            : "Brak importów wewnętrznych.";

        return $"Cel projektu: {description}\n\nPliki (kolejność zależności):\n{fileList}{depContext}\n\n" +
               $"Napisz kompletny kod dla: {file.Path}\nCel pliku: {file.Description}\n{importsLine}\n\n" +
               $"Zasady: KOMPLETNY i URUCHAMIALNY kod, bez zaślepek, dokładnie zgodne ścieżki importów.\n\n" +
               $"Kod dla {file.Path}:";
    }

    [GeneratedRegex(@"[^\w\-]")]
    private static partial Regex NonNameChars();

    [GeneratedRegex(@"No module named ['""]([a-zA-Z0-9_\-\.]+)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex MissingModule();

    /// <summary>Plan projektu z planera (JSON).</summary>
    internal sealed record ProjectPlan
    {
        [JsonPropertyName("project_name")] public string ProjectName { get; init; } = "";
        [JsonPropertyName("entry_point")] public string EntryPoint { get; init; } = "main.py";
        [JsonPropertyName("run_command")] public string RunCommand { get; init; } = "";
        [JsonPropertyName("files")] public List<PlanFile> Files { get; init; } = [];
        [JsonPropertyName("dependencies")] public List<string> Dependencies { get; init; } = [];
    }

    /// <summary>Pojedynczy plik w planie.</summary>
    internal sealed record PlanFile
    {
        [JsonPropertyName("path")] public string Path { get; init; } = "";
        [JsonPropertyName("description")] public string Description { get; init; } = "";
        [JsonPropertyName("imports")] public List<string> Imports { get; init; } = [];
    }
}
