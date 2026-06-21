using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ToperJarvis.App.Services;

/// <summary>
/// Zapisuje pojedyncze ustawienia użytkownika do <c>appsettings.Local.json</c> (obok exe),
/// zachowując pozostałe klucze. Plik jest też źródłem konfiguracji przy następnym starcie.
/// </summary>
public sealed class LocalSettingsWriter(ILogger<LocalSettingsWriter> logger)
{
    private static readonly string Path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Ustawia wartość pod ścieżką klucza (np. "Jarvis", "Audio", "InputDeviceName").</summary>
    public void Set(string value, params string[] path)
    {
        if (path.Length == 0)
            throw new ArgumentException("Ścieżka klucza nie może być pusta.", nameof(path));

        try
        {
            var root = ReadRoot();
            var node = root;
            for (var i = 0; i < path.Length - 1; i++)
            {
                if (node[path[i]] is not JsonObject child)
                {
                    child = new JsonObject();
                    node[path[i]] = child;
                }

                node = child;
            }

            node[path[^1]] = value;
            File.WriteAllText(Path, root.ToJsonString(WriteOptions));
        }
        catch (Exception ex)
        {
            // Brak zapisu nie jest krytyczny — wybór i tak działa do końca sesji.
            logger.LogWarning(ex, "Nie udało się zapisać ustawienia do {Path}.", Path);
        }
    }

    private static JsonObject ReadRoot()
    {
        if (!File.Exists(Path))
            return new JsonObject();

        var text = File.ReadAllText(Path);
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
    }
}
