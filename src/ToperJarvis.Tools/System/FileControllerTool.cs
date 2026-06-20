using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>file_controller</c> — operacje na plikach i folderach (lista, odczyt, zapis,
/// tworzenie, usuwanie, przenoszenie, kopiowanie, zmiana nazwy, wyszukiwanie). Dla bezpieczeństwa
/// operacje są ograniczone do katalogu domowego użytkownika.
/// </summary>
public sealed class FileControllerTool : IJarvisTool
{
    private static readonly string Home =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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
        try
        {
            return action.ToLowerInvariant() switch
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
            return "Brak uprawnień do tej ścieżki.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd operacji {Action} na {Path}.", action, path);
            return $"Błąd operacji {action}: {ex.Message}";
        }
    }

    /// <summary>Sprowadza ścieżkę do bezwzględnej i waliduje, że leży w katalogu domowym.</summary>
    internal static string Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Home;

        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Home, path));
        var homeFull = Path.GetFullPath(Home);
        if (!full.StartsWith(homeFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Ścieżka poza katalogiem domowym.");

        return full;
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
        var src = Resolve(path);
        var dst = Resolve(destination);
        File.Copy(src, dst, overwrite: true);
        return $"Skopiowano do: {dst}";
    }

    private static string Rename(string? path, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return "Nie podano nowej nazwy.";

        var src = Resolve(path);
        var dst = Resolve(Path.Combine(Path.GetDirectoryName(src) ?? Home, newName));
        if (Directory.Exists(src))
            Directory.Move(src, dst);
        else
            File.Move(src, dst);
        return $"Zmieniono nazwę na: {Path.GetFileName(dst)}";
    }

    private static string Find(string? path, string? pattern)
    {
        var dir = Resolve(path);
        if (string.IsNullOrWhiteSpace(pattern))
            return "Nie podano wzorca wyszukiwania.";

        var matches = Directory.EnumerateFileSystemEntries(dir, "*" + pattern + "*", SearchOption.AllDirectories)
            .Take(50)
            .Select(e => e[(Home.Length + 1)..])
            .ToList();

        return matches.Count == 0 ? "Nie znaleziono." : string.Join("\n", matches);
    }
}
