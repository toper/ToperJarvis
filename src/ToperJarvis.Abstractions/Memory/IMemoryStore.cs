namespace ToperJarvis.Abstractions.Memory;

/// <summary>
/// Pamięć długoterminowa asystenta — fakty o użytkowniku w 6 kategoriach: identity, preferences,
/// projects, relationships, wishes, notes. Trwała (JSON na dysku).
/// </summary>
public interface IMemoryStore
{
    /// <summary>Zapisuje/aktualizuje fakt. Zwraca krótkie potwierdzenie.</summary>
    string Remember(string key, string value, string category = "notes");

    /// <summary>Usuwa fakt. Zwraca krótkie potwierdzenie.</summary>
    string Forget(string key, string category = "notes");

    /// <summary>
    /// Formatuje zapamiętane fakty do wstrzyknięcia w prompt systemowy. Pusty string, gdy brak.
    /// </summary>
    string FormatForPrompt();
}
