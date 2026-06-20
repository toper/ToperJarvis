using System.Text;
using System.Text.RegularExpressions;

namespace ToperJarvis.Llm;

/// <summary>
/// Akumuluje strumień tekstu z LLM i wydziela kompletne zdania, gdy tylko się pojawią — dzięki
/// czemu synteza TTS może ruszyć ze zdaniem 1, zanim model dokończy zdanie 2 (nakładanie latencji).
/// Granica zdania: znak [.!?] + biały znak lub pusta linia (nie tnie liczb jak „3.5").
/// </summary>
public sealed partial class SentenceAccumulator
{
    private readonly StringBuilder _buffer = new();

    [GeneratedRegex(@"(?<=[.!?])\s+|(?<=\n)\s*\n")]
    private static partial Regex SentenceBoundary();

    /// <summary>Dodaje kolejny fragment tekstu i zwraca wszystkie domknięte zdania.</summary>
    public IEnumerable<string> Add(string delta)
    {
        if (string.IsNullOrEmpty(delta))
            yield break;

        _buffer.Append(delta);

        while (true)
        {
            var text = _buffer.ToString();
            var match = SentenceBoundary().Match(text);
            if (!match.Success)
                yield break;

            var sentence = text[..match.Index].Trim();
            _buffer.Clear();
            _buffer.Append(text[(match.Index + match.Length)..]);

            if (sentence.Length > 0)
                yield return sentence;
        }
    }

    /// <summary>Zwraca pozostałą, niedomkniętą treść (np. po zakończeniu strumienia) lub null.</summary>
    public string? Flush()
    {
        var remaining = _buffer.ToString().Trim();
        _buffer.Clear();
        return remaining.Length > 0 ? remaining : null;
    }
}
