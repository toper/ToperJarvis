using System.ComponentModel;
using Microsoft.Extensions.AI;
using ToperJarvis.Abstractions.Memory;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.Memory;

/// <summary>
/// Narzędzie <c>save_memory</c> — zapisuje trwały fakt o użytkowniku w pamięci długoterminowej.
/// </summary>
public sealed class SaveMemoryTool : IJarvisTool
{
    private readonly IMemoryStore _memory;

    public SaveMemoryTool(IMemoryStore memory) => _memory = memory;

    public string Name => "save_memory";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(Save, Name,
            "Zapisuje trwały fakt o użytkowniku do pamięci długoterminowej. Używaj, gdy użytkownik " +
            "podaje informacje o sobie warte zapamiętania (imię, preferencje, projekty, plany).");

    [Description("Zapisuje fakt o użytkowniku.")]
    private string Save(
        [Description("Krótki klucz faktu, np. 'imie', 'ulubiony_jezyk'.")] string key,
        [Description("Wartość faktu do zapamiętania.")] string value,
        [Description("Kategoria: identity, preferences, projects, relationships, wishes lub notes.")]
        string category = "notes")
        => _memory.Remember(key, value, category);
}
