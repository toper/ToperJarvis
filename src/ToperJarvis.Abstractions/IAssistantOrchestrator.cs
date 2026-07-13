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
/// Strumieniowy fragment odpowiedzi asystenta — tekst skumulowany dotychczas. <see cref="IsFinal"/>
/// oznacza ostatnią aktualizację tury (pełny tekst). Pozwala UI pokazywać odpowiedź na bieżąco,
/// równolegle z czytaniem przez TTS, zamiast dopiero po wygenerowaniu całości.
/// </summary>
public readonly record struct AssistantStreamChunk(string Text, bool IsFinal);

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

    /// <summary>
    /// Zgłaszane wielokrotnie w trakcie generowania odpowiedzi asystenta (tekst skumulowany).
    /// Umożliwia wyświetlanie odpowiedzi na bieżąco, równolegle z TTS.
    /// </summary>
    event EventHandler<AssistantStreamChunk>? AssistantStreaming;

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

    /// <summary>Przerywa bieżącą turę (myślenie/akcje/mowę) — anuluje wywołanie LLM i odtwarzanie TTS.</summary>
    void Interrupt();

    /// <summary>
    /// Czyści kontekst rozmowy — kasuje całą historię wysyłaną do mózgu (Hektora) i przerywa bieżącą
    /// turę. Kolejna komenda startuje „od zera" (z ponownym system promptem). Zapobiega kumulowaniu się
    /// długiej/zafałszowanej historii, która potrafi zbić agenta z tropu (np. brnięcie w zmyślone dane).
    /// </summary>
    void ClearContext();
}
