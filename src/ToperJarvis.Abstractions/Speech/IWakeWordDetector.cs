namespace ToperJarvis.Abstractions.Speech;

/// <summary>
/// Detektor słowa-klucza („Jarvis"). Analizuje ciągły strumień audio i zgłasza
/// <see cref="Detected"/>, gdy rozpozna słowo wybudzające.
/// </summary>
public interface IWakeWordDetector : IDisposable
{
    /// <summary>Zgłaszane po wykryciu słowa-klucza.</summary>
    event EventHandler? Detected;

    /// <summary>Rozpoczyna nasłuch słowa-klucza.</summary>
    void Start();

    /// <summary>Zatrzymuje nasłuch.</summary>
    void Stop();
}
