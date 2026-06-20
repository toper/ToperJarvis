using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Memory;

namespace ToperJarvis.Core.Memory;

/// <summary>
/// Pamięć długoterminowa zapisywana w pliku JSON (port <c>memory/memory_manager.py</c>).
/// 6 kategorii, każda jako mapa klucz → {value, updated}. Wartości przycinane do limitu,
/// całość ograniczona rozmiarowo (usuwane najstarsze wpisy).
/// </summary>
public sealed class JsonMemoryStore : IMemoryStore
{
    private const int MaxValueLength = 380;
    private const int MaxChars = 2200;

    private static readonly string[] Categories =
        { "identity", "preferences", "projects", "relationships", "wishes", "notes" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly ILogger<JsonMemoryStore> _logger;
    private readonly object _lock = new();

    public JsonMemoryStore(ILogger<JsonMemoryStore> logger)
        : this(Path.Combine("memory", "long_term.json"), logger) { }

    public JsonMemoryStore(string path, ILogger<JsonMemoryStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    private sealed class Entry
    {
        public string Value { get; set; } = "";
        public string Updated { get; set; } = "";
    }

    // kategoria -> (klucz -> wpis)
    private Dictionary<string, Dictionary<string, Entry>> Load()
    {
        var memory = Categories.ToDictionary(c => c, _ => new Dictionary<string, Entry>());
        if (!File.Exists(_path))
            return memory;

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Entry>>>(
                File.ReadAllText(_path), JsonOptions);
            if (loaded is not null)
                foreach (var (cat, items) in loaded)
                    if (memory.ContainsKey(cat))
                        memory[cat] = items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd wczytywania pamięci — używam pustej.");
        }

        return memory;
    }

    private void Save(Dictionary<string, Dictionary<string, Entry>> memory)
    {
        TrimToLimit(memory);
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(memory, JsonOptions));
    }

    private static void TrimToLimit(Dictionary<string, Dictionary<string, Entry>> memory)
    {
        while (JsonSerializer.Serialize(memory, JsonOptions).Length > MaxChars)
        {
            // usuń najstarszy wpis (po dacie aktualizacji)
            var oldest = memory
                .SelectMany(cat => cat.Value.Select(kv => (cat: cat.Key, key: kv.Key, kv.Value.Updated)))
                .OrderBy(t => t.Updated, StringComparer.Ordinal)
                .FirstOrDefault();

            if (oldest.cat is null)
                break;

            memory[oldest.cat].Remove(oldest.key);
        }
    }

    public string Remember(string key, string value, string category = "notes")
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return "Pominięto: pusty klucz lub wartość.";

        if (!Categories.Contains(category))
            category = "notes";

        var trimmed = value.Length > MaxValueLength
            ? value[..MaxValueLength].TrimEnd() + "…"
            : value;

        lock (_lock)
        {
            var memory = Load();
            memory[category][key] = new Entry
            {
                Value = trimmed,
                Updated = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            };
            Save(memory);
        }

        return $"Zapamiętano: {category}/{key} = {trimmed}";
    }

    public string Forget(string key, string category = "notes")
    {
        if (!Categories.Contains(category))
            category = "notes";

        lock (_lock)
        {
            var memory = Load();
            if (memory[category].Remove(key))
            {
                Save(memory);
                return $"Zapomniano: {category}/{key}";
            }
        }

        return $"Nie znaleziono: {category}/{key}";
    }

    public string FormatForPrompt()
    {
        Dictionary<string, Dictionary<string, Entry>> memory;
        lock (_lock)
            memory = Load();

        var sb = new StringBuilder();
        foreach (var category in Categories)
        {
            var items = memory[category];
            if (items.Count == 0)
                continue;

            sb.AppendLine($"{category}:");
            foreach (var (key, entry) in items)
                sb.AppendLine($"  - {key}: {entry.Value}");
        }

        if (sb.Length == 0)
            return string.Empty;

        var header = "[CO WIESZ O TEJ OSOBIE — używaj naturalnie, nie wymieniaj jak listy]\n";
        var result = header + sb.ToString().TrimEnd();
        return result.Length > 2000 ? result[..1997] + "…" : result;
    }
}
