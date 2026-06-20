using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>file_controller</c> — operacje na plikach i folderach (lista, odczyt, zapis,
/// tworzenie, usuwanie, przenoszenie, kopiowanie, zmiana nazwy, wyszukiwanie). Dla bezpieczeństwa
/// operacje są ograniczone do katalogu domowego użytkownika (z walidacją granicy i symlinków).
/// </summary>
public sealed class FileControllerTool : IJarvisTool
{
    private static readonly string HomeFull =
        Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private readonly ILogger<FileControllerTool> _logger;

    public FileControllerTool(ILogger<FileControllerTool> logger) => _logger = logger;

    public string Name => "file_controller";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Execute, Name,
            "Zarządza plikami i folderami w katalogu użytkownika: list, read, write, create_file, " +
            "create_folder, delete, move, copy, rename, find.");

    [Description("Wykonuje operację na pliku/folderze.")]
    private string Execute(
        [Description("Operacja: list, read, write, create_file, create_folder, delete, move, copy, rename, find.")]
        string action,
        [Description("Ścieżka pliku/folderu (względna do katalogu domowego lub bezwzględna w jego obrębie).")]
        string? path = null,
        [Description("Ścieżka docelowa (dla move/copy).")] string? destination = null,
        [Description("Nowa nazwa (dla rename) lub wzorzec (dla find).")] string? newName = null,
        [Description("Treść do zapisania (dla write/create_file).")] string? content = null)
    {
        var op = (action ?? string.Empty).Trim().ToLowerInvariant();
        try
        {
            // Tylko "list" może działać domyślnie na katalogu domowym; reszta wymaga jawnej ścieżki.
            if (op != "list" && string.IsNullOrWhiteSpace(path))
                return $"Operacja '{op}' wymaga podania ścieżki.";

            return op switch
            {
                "list" => List(path),
                "read" => Read(path),
                "write" or "create_file" => Write(path, content),
                "create_folder" => CreateFolder(path),
                "delete" => Delete(path),
                "move" => Move(path, destination),
                "copy" => Copy(path, destination),
                "rename" => Rename(path, newName),
                "find" => Find(path, newName),
                _ => $"Nieznana operacja: {action}.",
            };
        }
        catch (UnauthorizedAccessException)
        {
            return "Brak uprawnień lub ścieżka poza katalogiem domowym.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd operacji {Action} na {Path}.", op, path);
            return $"Błąd operacji {op}: {ex.Message}";
        }
    }

    /// <summary>
    /// Sprowadza ścieżkę do bezwzględnej i waliduje, że leży w katalogu domowym — z poprawnym
    /// sprawdzeniem granicy (separator) oraz rozwinięciem symlinków/junctions istniejącego celu.
    /// </summary>
    internal static string Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return HomeFull;

        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(HomeFull, path));
        EnsureWithinHome(full);

        // Ochrona przed ucieczką przez symlink/junction: jeśli cel ISTNIEJE i jest dowiązaniem,
        // zweryfikuj jego rzeczywistą lokalizację. Dla nieistniejących ścieżek (np. przy zapisie)
        // pomijamy — nie ma czego rozwijać.
        FileSystemInfo? info =
            Directory.Exists(full) ? new DirectoryInfo(full) :
            File.Exists(full) ? new FileInfo(full) : null;

        var realTarget = info?.ResolveLinkTarget(returnFinalTarget: true);
        if (realTarget is not null)
            EnsureWithinHome(Path.GetFullPath(realTarget.FullName));

        return full;
    }

    private static void EnsureWithinHome(string full)
    {
        if (string.Equals(full, HomeFull, StringComparison.OrdinalIgnoreCase))
            return;

        var prefix = HomeFull.EndsWith(Path.DirectorySeparatorChar)
            ? HomeFull
            : HomeFull + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Ścieżka poza katalogiem domowym.");
    }

    private static string List(string? path)
    {
        var dir = Resolve(path);
        if (!Directory.Exists(dir))
            return $"Folder nie istnieje: {dir}";

        var entries = Directory.EnumerateFileSystemEntries(dir)
            .Select(e => (Directory.Exists(e) ? "[DIR] " : "      ") + Path.GetFileName(e))
            .Take(100)
            .ToList();

        return entries.Count == 0 ? "Folder jest pusty." : string.Join("\n", entries);
    }

    private static string Read(string? path)
    {
        var file = Resolve(path);
        if (!File.Exists(file))
            return $"Plik nie istnieje: {file}";

        var text = File.ReadAllText(file);
        return text.Length > 4000 ? text[..4000] + "…" : text;
    }

    private static string Write(string? path, string? content)
    {
        var file = Resolve(path);
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(file, content ?? string.Empty);
        return $"Zapisano: {file}";
    }

    private static string CreateFolder(string? path)
    {
        var dir = Resolve(path);
        Directory.CreateDirectory(dir);
        return $"Utworzono folder: {dir}";
    }

    private static string Delete(string? path)
    {
        var target = Resolve(path);
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
            return $"Usunięto folder: {target}";
        }
        if (File.Exists(target))
        {
            File.Delete(target);
            return $"Usunięto plik: {target}";
        }
        return $"Nie istnieje: {target}";
    }

    private static string Move(string? path, string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return "Operacja 'move' wymaga ścieżki docelowej.";

        var src = Resolve(path);
        var dst = Resolve(destination);
        if (Directory.Exists(src))
            Directory.Move(src, dst);
        else
            File.Move(src, dst, overwrite: true);
        return $"Przeniesiono do: {dst}";
    }

    private static string Copy(string? path, string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return "Operacja 'copy' wymaga ścieżki docelowej.";

        var src = Resolve(path);
        var dst = Resolve(destination);

        if (Directory.Exists(src))
        {
            CopyDirectory(src, dst);
            return $"Skopiowano folder do: {dst}";
        }

        var dstDir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dstDir))
            Directory.CreateDirectory(dstDir);
        File.Copy(src, dst, overwrite: true);
        return $"Skopiowano do: {dst}";
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(src, dst), overwrite: true);
    }

    private static string Rename(string? path, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return "Nie podano nowej nazwy.";

        var src = Resolve(path);
        var dst = Resolve(Path.Combine(Path.GetDirectoryName(src) ?? HomeFull, newName));
        if (Directory.Exists(src))
            Directory.Move(src, dst);
        else
            File.Move(src, dst);
        return $"Zmieniono nazwę na: {Path.GetFileName(dst)}";
    }

    private static string Find(string? path, string? pattern)
    {
        var dir = Resolve(path);
        if (!Directory.Exists(dir))
            return $"Folder nie istnieje: {dir}";
        if (string.IsNullOrWhiteSpace(pattern))
            return "Nie podano wzorca wyszukiwania.";

        var matches = Directory.EnumerateFileSystemEntries(dir, "*" + pattern + "*", SearchOption.AllDirectories)
            .Take(50)
            .Select(e => Path.GetRelativePath(HomeFull, e))
            .ToList();

        return matches.Count == 0 ? "Nie znaleziono." : string.Join("\n", matches);
    }
}
