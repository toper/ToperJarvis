namespace ToperJarvis.Core.Prompting;

/// <summary>
/// Dostarcza prompt systemowy dla asystenta. Jeśli istnieje plik <c>assets/prompt.txt</c>,
/// używa jego treści; w przeciwnym razie korzysta z wbudowanego domyślnego promptu (PL).
/// Do statycznej części doklejana jest dynamiczna data/godzina.
/// </summary>
public sealed class SystemPromptProvider
{
    private const string PromptPath = "assets/prompt.txt";

    private const string DefaultPrompt =
        """
        Jesteś Jarvis — uprzejmym, rzeczowym asystentem głosowym działającym lokalnie na komputerze użytkownika.
        Odpowiadaj po polsku, zwięźle (maksymalnie 2-3 zdania), naturalnym językiem mówionym — Twoja odpowiedź będzie odczytana na głos.
        Gdy zadanie wymaga działania na komputerze, użyj dostępnych narzędzi zamiast tylko opisywać czynność.
        Nie wymyślaj faktów. Jeśli czegoś nie wiesz lub nie możesz wykonać, powiedz to wprost.
        """;

    /// <summary>Buduje pełny prompt systemowy (statyczna baza + bieżąca data/godzina).</summary>
    public string Build(DateTimeOffset now)
    {
        var basePrompt = File.Exists(PromptPath)
            ? File.ReadAllText(PromptPath)
            : DefaultPrompt;

        var when = now.ToString("dddd, d MMMM yyyy, HH:mm",
            System.Globalization.CultureInfo.GetCultureInfo("pl-PL"));

        return $"{basePrompt}\n\nAktualna data i godzina: {when}.";
    }
}
