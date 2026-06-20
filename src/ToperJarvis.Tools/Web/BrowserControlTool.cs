using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Tools;

namespace ToperJarvis.Tools.Web;

/// <summary>
/// Narzędzie <c>browser_control</c> — steruje przeglądarką przez Playwright: nawigacja, kliknięcia,
/// wpisywanie, wyszukiwanie, odczyt treści, karty i zrzuty ekranu. Jedna trwała sesja Chromium
/// (profil dedykowany lub realny — konfiguracja). Port <c>_Old/actions/browser_control.py</c> (v1
/// bez rejestru wielu przeglądarek). Akcje „smart" lokalizują elementy po dostępności (role/label),
/// bez wizji.
/// </summary>
public sealed class BrowserControlTool : IJarvisTool, IAsyncDisposable
{
    private static readonly IReadOnlyDictionary<string, string> SearchEngines =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["google"] = "https://www.google.com/search?q=",
            ["bing"] = "https://www.bing.com/search?q=",
            ["duckduckgo"] = "https://duckduckgo.com/?q=",
            ["yandex"] = "https://yandex.com/search/?text=",
        };

    private const int ClickTimeoutMs = 8_000;
    private const int GotoTimeoutMs = 30_000;
    private const int MaxTextLength = 4_000;

    private readonly BrowserSession _session;

    public BrowserControlTool(IOptions<JarvisOptions> options, ILogger<BrowserControlTool> logger)
        => _session = new BrowserSession(options.Value.Browser, logger);

    public string Name => "browser_control";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(ExecuteAsync, Name,
            "Steruje przeglądarką: go_to (otwórz URL), search (wyszukaj), click, type, scroll, press, " +
            "fill_form, smart_click/smart_type (po opisie elementu), get_text, get_url, new_tab, " +
            "close_tab, back, forward, reload, screenshot, close.");

    [Description("Wykonuje akcję w przeglądarce.")]
    private Task<string> ExecuteAsync(
        [Description("Akcja: go_to, search, click, type, scroll, press, fill_form, smart_click, " +
                     "smart_type, get_text, get_url, new_tab, close_tab, back, forward, reload, screenshot, close.")]
        string action,
        [Description("Adres lub nazwa strony (dla go_to / new_tab).")] string? url = null,
        [Description("Zapytanie (dla search).")] string? query = null,
        [Description("Wyszukiwarka: google, bing, duckduckgo, yandex (dla search).")] string engine = "google",
        [Description("Selektor CSS elementu (dla click / type).")] string? selector = null,
        [Description("Tekst do kliknięcia (dla click) lub wpisania (dla type).")] string? text = null,
        [Description("Opis elementu w języku naturalnym (dla smart_click / smart_type).")] string? description = null,
        [Description("Klawisz do wciśnięcia, np. Enter (dla press).")] string key = "Enter",
        [Description("Kierunek przewijania: down lub up (dla scroll).")] string direction = "down",
        [Description("Wielkość przewinięcia w px (dla scroll).")] int amount = 500,
        [Description("Czy wyczyścić pole przed wpisaniem (dla type).")] bool clearFirst = true,
        [Description("Ścieżka pliku zrzutu ekranu (dla screenshot).")] string? path = null,
        [Description("Pary selektor→wartość do wypełnienia (dla fill_form).")]
        Dictionary<string, string>? fields = null,
        CancellationToken cancellationToken = default)
    {
        switch (action?.Trim().ToLowerInvariant())
        {
            case "go_to":
                return _session.RunAsync(p => GoToAsync(p, NormalizeUrl(url ?? "")), cancellationToken);

            case "search":
                return _session.RunAsync(p => GoToAsync(p, SearchUrl(query ?? "", engine)), cancellationToken);

            case "click":
                return _session.RunAsync(p => ClickAsync(p, selector, text), cancellationToken);

            case "type":
                return _session.RunAsync(p => TypeAsync(p, selector, text ?? "", clearFirst), cancellationToken);

            case "scroll":
                return _session.RunAsync(async p =>
                {
                    await p.Mouse.WheelAsync(0, string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase) ? -amount : amount);
                    return $"Przewinięto {direction}.";
                }, cancellationToken);

            case "press":
                return _session.RunAsync(async p =>
                {
                    await p.Keyboard.PressAsync(key);
                    return $"Wciśnięto: {key}.";
                }, cancellationToken);

            case "fill_form":
                return _session.RunAsync(p => FillFormAsync(p, fields), cancellationToken);

            case "smart_click":
                return _session.RunAsync(p => SmartClickAsync(p, description ?? ""), cancellationToken);

            case "smart_type":
                return _session.RunAsync(p => SmartTypeAsync(p, description ?? "", text ?? ""), cancellationToken);

            case "get_text":
                return _session.RunAsync(async p =>
                {
                    var body = await p.InnerTextAsync("body");
                    return body.Length > MaxTextLength ? body[..MaxTextLength] : body;
                }, cancellationToken);

            case "get_url":
                return _session.RunAsync(p => Task.FromResult(p.Url), cancellationToken);

            case "new_tab":
                return _session.NewTabAsync(string.IsNullOrWhiteSpace(url) ? "" : NormalizeUrl(url), cancellationToken);

            case "close_tab":
                return _session.CloseTabAsync(cancellationToken);

            case "back":
                return _session.RunAsync(async p => { await p.GoBackAsync(); return "Cofnięto."; }, cancellationToken);

            case "forward":
                return _session.RunAsync(async p => { await p.GoForwardAsync(); return "Do przodu."; }, cancellationToken);

            case "reload":
                return _session.RunAsync(async p => { await p.ReloadAsync(); return "Odświeżono."; }, cancellationToken);

            case "screenshot":
                return _session.RunAsync(async p =>
                {
                    var file = string.IsNullOrWhiteSpace(path)
                        ? Path.Combine(Path.GetTempPath(), $"jarvis-screenshot-{Guid.NewGuid():N}.png")
                        : path;
                    await p.ScreenshotAsync(new PageScreenshotOptions { Path = file });
                    return $"Zapisano zrzut ekranu: {file}.";
                }, cancellationToken);

            case "close":
                return _session.CloseAsync();

            default:
                return Task.FromResult($"Nieobsługiwana akcja przeglądarki: {action}.");
        }
    }

    private async Task<string> GoToAsync(IPage page, string url)
    {
        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = GotoTimeoutMs,
            });
        }
        catch (TimeoutException)
        {
            // strona mogła załadować się częściowo — sprawdzamy URL poniżej
        }

        var current = page.Url;
        return current is "about:blank" or ""
            ? $"Nie udało się otworzyć: {url}."
            : $"Otwarto: {current}.";
    }

    private async Task<string> ClickAsync(IPage page, string? selector, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            await page.GetByText(text, new PageGetByTextOptions { Exact = false })
                .First.ClickAsync(new LocatorClickOptions { Timeout = ClickTimeoutMs });
            return $"Kliknięto tekst: '{text}'.";
        }

        if (!string.IsNullOrWhiteSpace(selector))
        {
            await page.ClickAsync(selector, new PageClickOptions { Timeout = ClickTimeoutMs });
            return $"Kliknięto selektor: {selector}.";
        }

        return "Nie podano selektora ani tekstu.";
    }

    private async Task<string> TypeAsync(IPage page, string? selector, string text, bool clearFirst)
    {
        var element = string.IsNullOrWhiteSpace(selector) ? page.Locator(":focus") : page.Locator(selector).First;
        if (clearFirst)
            await element.ClearAsync();
        await element.PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = 50 });
        return "Wpisano tekst.";
    }

    private async Task<string> FillFormAsync(IPage page, Dictionary<string, string>? fields)
    {
        if (fields is null || fields.Count == 0)
            return "Brak pól do wypełnienia.";

        var results = new List<string>();
        foreach (var (selector, value) in fields)
        {
            try
            {
                var element = page.Locator(selector).First;
                await element.ClearAsync();
                await element.PressSequentiallyAsync(value, new LocatorPressSequentiallyOptions { Delay = 40 });
                results.Add($"✓ {selector}");
            }
            catch (Exception ex)
            {
                results.Add($"✗ {selector}: {ex.Message}");
            }
        }

        return "Wypełniono formularz: " + string.Join(", ", results);
    }

    private static readonly AriaRole[] SmartClickRoles =
        [AriaRole.Button, AriaRole.Link, AriaRole.Searchbox, AriaRole.Textbox, AriaRole.Menuitem, AriaRole.Tab];

    private async Task<string> SmartClickAsync(IPage page, string description)
    {
        foreach (var role in SmartClickRoles)
        {
            var locator = page.GetByRole(role, new PageGetByRoleOptions { Name = description });
            if (await locator.CountAsync() > 0)
            {
                await locator.First.ClickAsync(new LocatorClickOptions { Timeout = ClickTimeoutMs });
                return $"Kliknięto ({role}): '{description}'.";
            }
        }

        try
        {
            await page.GetByText(description, new PageGetByTextOptions { Exact = false })
                .First.ClickAsync(new LocatorClickOptions { Timeout = ClickTimeoutMs });
            return $"Kliknięto: '{description}'.";
        }
        catch (Exception)
        {
            return $"Nie znaleziono elementu: '{description}'.";
        }
    }

    private async Task<string> SmartTypeAsync(IPage page, string description, string text)
    {
        ILocator[] candidates =
        [
            page.GetByPlaceholder(description, new PageGetByPlaceholderOptions { Exact = false }),
            page.GetByLabel(description, new PageGetByLabelOptions { Exact = false }),
            page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = description }),
            page.GetByRole(AriaRole.Combobox, new PageGetByRoleOptions { Name = description }),
        ];

        foreach (var candidate in candidates)
        {
            var element = candidate.First;
            if (await candidate.CountAsync() == 0)
                continue;

            await element.ClearAsync();
            await element.PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = 50 });
            return $"Wpisano w pole: '{description}'.";
        }

        return $"Nie znaleziono pola: '{description}'.";
    }

    /// <summary>Goła nazwa („instagram") → „https://instagram.com"; domena → https://; pełny URL bez zmian.</summary>
    internal static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.Length == 0)
            return "about:blank";
        if (url.Contains("://", StringComparison.Ordinal))
            return url;
        if (!url.Contains('.'))
            url += ".com";
        return "https://" + url;
    }

    /// <summary>Buduje URL wyszukiwania dla podanej wyszukiwarki (domyślnie Google).</summary>
    internal static string SearchUrl(string query, string engine)
    {
        var baseUrl = SearchEngines.TryGetValue(engine ?? "", out var url) ? url : SearchEngines["google"];
        return baseUrl + Uri.EscapeDataString(query.Trim());
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
