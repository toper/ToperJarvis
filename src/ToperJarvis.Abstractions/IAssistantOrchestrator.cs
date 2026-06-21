namespace ToperJarvis.Abstractions;

/// <summary>Rola autora wpisu w konwersacji.</summary>
public enum TranscriptRole
{
    User,
    Assistant,
    System,
}

/// <summary>Pojedynczy wpis konwersacji (do prezentacji w UI/logu).</summary>
public readonly record struct TranscriptEntry(TranscriptRole Role, string Text);

/// <summary>
/// Centralny orchestrator pętli głosowej: wake-word → nasłuch (VAD) → STT → LLM (tool-calling)
/// → TTS. Udostępnia bieżący stan i zdarzenia dla warstwy UI.
/// </summary>
public interface IAssistantOrchestrator
{
    /// <summary>Bieżący stan asystenta.</summary>
    AssistantState State { get; }

    /// <summary>Zgłaszane przy każdej zmianie stanu.</summary>
    event EventHandler<AssistantState>? StateChanged;

    /// <summary>Zgłaszane po dodaniu wpisu do konwersacji (użytkownik/asystent).</summary>
    event EventHandler<TranscriptEntry>? TranscriptAdded;

    /// <summary>Zgłaszane po zakończeniu tury (przetworzeniu komendy) z czasem w milisekundach.</summary>
    event EventHandler<double>? TurnCompleted;

    /// <summary>Uruchamia przechwytywanie audio i nasłuch słowa-klucza.</summary>
    void Start();

    /// <summary>Zatrzymuje pętlę.</summary>
    void Stop();

    /// <summary>Przetwarza komendę wpisaną tekstem (z pominięciem STT/wake-word).</summary>
    Task SubmitTextAsync(string text, CancellationToken ct = default);

    /// <summary>Push-to-talk: początek nasłuchu (przytrzymano klawisz). Nagrywa do puszczenia.</summary>
    void BeginPushToTalk();

    /// <summary>Push-to-talk: koniec nasłuchu (puszczono klawisz) — przetwarza nagranie.</summary>
    void EndPushToTalk();
}
