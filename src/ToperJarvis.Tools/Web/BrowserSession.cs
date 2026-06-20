using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.Tools.Web;

/// <summary>
/// Trwała sesja przeglądarki sterowana przez Playwright (persistent context). Uruchamiana leniwie
/// przy pierwszej akcji; dostęp serializowany (jeden bufor zdarzeń UI). Port logiki
/// <c>_BrowserSession</c> z <c>_Old/actions/browser_control.py</c> (jedna przeglądarka, bez rejestru wielu).
/// </summary>
internal sealed class BrowserSession : IAsyncDisposable
{
    private readonly BrowserOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private IPage? _page;

    public BrowserSession(BrowserOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Wykonuje operację na aktywnej stronie (z leniwym startem przeglądarki), serializowaną.</summary>
    public async Task<string> RunAsync(Func<IPage, Task<string>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var page = await EnsurePageAsync();
            return await action(page);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd akcji przeglądarki.");
            return $"Błąd przeglądarki: {ex.Message}";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Otwiera nową kartę (opcjonalnie pod adresem) i czyni ją aktywną.</summary>
    public async Task<string> NewTabAsync(string url, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsurePageAsync();
            _page = await _context!.NewPageAsync();
            if (!string.IsNullOrWhiteSpace(url))
                await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            return "Otwarto nową kartę.";
        }
        catch (Exception ex)
        {
            return $"Błąd przeglądarki: {ex.Message}";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Zamyka aktywną kartę i przełącza się na pozostałą (jeśli jest).</summary>
    public async Task<string> CloseTabAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_context is null || _page is null)
                return "Brak otwartej karty.";

            await _page.CloseAsync();
            _page = _context.Pages.Count > 0 ? _context.Pages[^1] : null;
            return "Zamknięto kartę.";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Zamyka przeglądarkę i zwalnia zasoby Playwright.</summary>
    public async Task<string> CloseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DisposeContextAsync();
            return "Zamknięto przeglądarkę.";
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IPage> EnsurePageAsync()
    {
        if (_context is null)
        {
            _playwright = await Playwright.CreateAsync();

            var userDataDir = string.IsNullOrWhiteSpace(_options.UserDataDir)
                ? DefaultProfileDir()
                : _options.UserDataDir;
            Directory.CreateDirectory(userDataDir);

            var launchOptions = new BrowserTypeLaunchPersistentContextOptions { Headless = _options.Headless };
            if (!string.IsNullOrWhiteSpace(_options.Channel))
                launchOptions.Channel = _options.Channel;

            _context = await _playwright.Chromium.LaunchPersistentContextAsync(userDataDir, launchOptions);
            _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
            _logger.LogInformation("Przeglądarka uruchomiona (profil: {Dir}).", userDataDir);
        }

        if (_page is null || _page.IsClosed)
            _page = await _context.NewPageAsync();

        return _page;
    }

    private async Task DisposeContextAsync()
    {
        if (_context is not null)
        {
            try { await _context.CloseAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Zamknięcie kontekstu przeglądarki."); }
        }
        _context = null;
        _page = null;
        _playwright?.Dispose();
        _playwright = null;
    }

    /// <summary>Domyślny dedykowany profil: <c>%LOCALAPPDATA%\ToperJarvis\browser</c>.</summary>
    internal static string DefaultProfileDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToperJarvis", "browser");

    public async ValueTask DisposeAsync()
    {
        await DisposeContextAsync();
        _gate.Dispose();
    }
}
