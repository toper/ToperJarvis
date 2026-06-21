namespace ToperJarvis.Abstractions;

/// <summary>
/// Globalny skrót „push-to-talk" (hold-to-talk): wciśnięcie i puszczenie klawisza zgłaszane są
/// niezależnie od fokusu okna. Implementacja platformowa (Windows: low-level keyboard hook).
/// </summary>
public interface IPushToTalkHotkey : IDisposable
{
    /// <summary>Klawisz wciśnięty — start nasłuchu.</summary>
    event EventHandler? Pressed;

    /// <summary>Klawisz puszczony — koniec nagrania.</summary>
    event EventHandler? Released;

    /// <summary>Instaluje hook (no-op, gdy wyłączone w konfiguracji).</summary>
    void Start();

    /// <summary>Usuwa hook.</summary>
    void Stop();
}
