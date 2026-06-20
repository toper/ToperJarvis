namespace ToperJarvis.Abstractions.Speech;

/// <summary>Synteza mowy (offline). Zamienia tekst na mowę i odtwarza ją.</summary>
public interface ITextToSpeech
{
    /// <summary>Syntetyzuje i odtwarza podany tekst. Kończy się po zakończeniu odtwarzania.</summary>
    Task SpeakAsync(string text, CancellationToken ct = default);
}
