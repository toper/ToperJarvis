namespace ToperJarvis.Abstractions;

/// <summary>Stan asystenta w pętli głosowej.</summary>
public enum AssistantState
{
    /// <summary>Bezczynny — czeka na słowo-klucz „Jarvis".</summary>
    Idle,

    /// <summary>Nasłuchuje wypowiedzi po wykryciu słowa-klucza (VAD aktywny).</summary>
    Listening,

    /// <summary>Transkrybuje nagraną wypowiedź (STT).</summary>
    Transcribing,

    /// <summary>Przetwarza zapytanie przez LLM (z ewentualnym tool-callingiem).</summary>
    Thinking,

    /// <summary>Odtwarza odpowiedź głosową (TTS).</summary>
    Speaking,
}
