using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Tools;
using ToperJarvis.Tools.Input;

namespace ToperJarvis.Tools.System;

/// <summary>
/// Narzędzie <c>send_message</c> — wysyła wiadomość przez komunikator, sterując pulpitem
/// (symulacja klawiatury). Aplikacje desktopowe (WhatsApp, Telegram, Signal, Discord) otwiera
/// z menu Start i wyszukuje odbiorcę; Instagram i Messenger otwiera w przeglądarce.
/// Port <c>_Old/actions/send_message.py</c>.
/// </summary>
public sealed class SendMessageTool : IJarvisTool
{
    /// <summary>Sposób dostarczenia wiadomości — każdy kanał ma własną sekwencję sterowania UI.</summary>
    internal enum Channel
    {
        DesktopApp,
        Instagram,
        Messenger,
    }

    /// <summary>Rozpoznana platforma: kanał + cel (nazwa aplikacji albo adres URL).</summary>
    internal readonly record struct PlatformSpec(Channel Channel, string Target);

    // Opóźnienia na reakcję UI (ms). Wartości empiryczne — komunikatory ładują się/renderują różnie.
    private const int StartMenuWaitMs = 500;
    private const int AppNameTypedWaitMs = 600;
    private const int AppLaunchWaitMs = 2500;
    private const int UiSettleMs = 500;
    private const int ClearSelectMs = 150;
    private const int SearchResolveMs = 1000;
    private const int ChatOpenMs = 800;
    private const int KeySettleMs = 200;
    private const int PageLoadMs = 4000;
    private const int PickContactMs = 1500;
    private const int TabStepMs = 150;
    private const int ConversationOpenMs = 2000;

    private static readonly IReadOnlyDictionary<string, PlatformSpec> Platforms =
        new Dictionary<string, PlatformSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["whatsapp"] = new(Channel.DesktopApp, "WhatsApp"),
            ["wapp"] = new(Channel.DesktopApp, "WhatsApp"),
            ["wp"] = new(Channel.DesktopApp, "WhatsApp"),
            ["telegram"] = new(Channel.DesktopApp, "Telegram"),
            ["tg"] = new(Channel.DesktopApp, "Telegram"),
            ["signal"] = new(Channel.DesktopApp, "Signal"),
            ["discord"] = new(Channel.DesktopApp, "Discord"),
            ["instagram"] = new(Channel.Instagram, "https://www.instagram.com/direct/new/"),
            ["insta"] = new(Channel.Instagram, "https://www.instagram.com/direct/new/"),
            ["ig"] = new(Channel.Instagram, "https://www.instagram.com/direct/new/"),
            ["messenger"] = new(Channel.Messenger, "https://www.messenger.com/"),
            ["facebook"] = new(Channel.Messenger, "https://www.messenger.com/"),
            ["fb"] = new(Channel.Messenger, "https://www.messenger.com/"),
        };

    private readonly ILogger<SendMessageTool> _logger;

    public SendMessageTool(ILogger<SendMessageTool> logger) => _logger = logger;

    public string Name => "send_message";

    public AIFunction AsAIFunction() =>
        AIFunctionFactory.Create(SendAsync, Name,
            "Wysyła wiadomość do odbiorcy przez komunikator (whatsapp, telegram, signal, discord, " +
            "instagram, messenger). Steruje pulpitem — wymaga zainstalowanej aplikacji lub zalogowanej sesji.");

    [Description("Wysyła wiadomość przez komunikator.")]
    private async Task<string> SendAsync(
        [Description("Nazwa odbiorcy (kontakt do wyszukania).")] string receiver,
        [Description("Treść wiadomości do wysłania.")] string messageText,
        [Description("Platforma: whatsapp, telegram, signal, discord, instagram lub messenger.")]
        string platform = "whatsapp",
        CancellationToken cancellationToken = default)
    {
        // Wpisywanie znak-po-znaku interpretuje nową linię jako „wyślij" — spłaszczamy do spacji,
        // żeby wieloliniowa wiadomość nie poszła w kawałkach (oryginał wklejał atomowo ze schowka).
        receiver = Flatten(receiver);
        messageText = Flatten(messageText);
        platform = string.IsNullOrWhiteSpace(platform) ? "whatsapp" : platform.Trim();

        if (receiver.Length == 0)
            return "Podaj odbiorcę wiadomości.";
        if (messageText.Length == 0)
            return "Podaj treść wiadomości.";

        var spec = ResolvePlatform(platform);
        _logger.LogInformation("send_message: {Platform} → {Receiver}.", platform, receiver);

        try
        {
            return spec.Channel switch
            {
                Channel.Instagram => await SendViaInstagramAsync(spec.Target, receiver, messageText, cancellationToken),
                Channel.Messenger => await SendViaMessengerAsync(spec.Target, receiver, messageText, cancellationToken),
                _ => await SendViaDesktopAppAsync(spec.Target, receiver, messageText, cancellationToken),
            };
        }
        catch (OperationCanceledException)
        {
            return "Wysyłanie wiadomości przerwane.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się wysłać wiadomości przez {Platform}.", platform);
            return $"Nie udało się wysłać wiadomości: {ex.Message}";
        }
    }

    /// <summary>
    /// Mapuje nazwę platformy na kanał i cel. Dopasowanie po dokładnym tokenie (LLM podaje pojedynczą
    /// nazwę/alias) — w odróżnieniu od oryginału, który dopasowywał po fragmencie i mylił np. „signal"
    /// z „instagram" (bo zawiera „ig"). Nierozpoznana platforma = aplikacja desktopowa o tej nazwie.
    /// </summary>
    internal static PlatformSpec ResolvePlatform(string platform)
    {
        var key = platform.Trim();
        if (Platforms.TryGetValue(key, out var spec))
            return spec;

        // Fallback: nieznana platforma = aplikacja desktopowa o tej nazwie (z wielkiej litery).
        var appName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.ToLowerInvariant());
        return new PlatformSpec(Channel.DesktopApp, appName);
    }

    private async Task<string> SendViaDesktopAppAsync(
        string appName, string receiver, string message, CancellationToken ct)
    {
        await OpenFromStartMenuAsync(appName, ct);
        await Task.Delay(SearchResolveMs, ct);

        await SearchContactAsync(receiver, ct);
        Press("enter"); // wybór pierwszego wyniku otwiera konwersację
        await Task.Delay(ChatOpenMs, ct);

        await SendTextAsync(message, ct);
        return $"Wiadomość wysłana do {receiver} przez {appName}.";
    }

    private async Task<string> SendViaInstagramAsync(
        string url, string receiver, string message, CancellationToken ct)
    {
        if (!OpenUrl(url))
            return "Nie udało się otworzyć Instagrama w przeglądarce.";

        await Task.Delay(PageLoadMs, ct);

        // Okno „New message": wpisz odbiorcę, zaznacz pierwszy wynik, przejdź do „Chat" i otwórz.
        InputSimulator.TypeText(receiver);
        await Task.Delay(PickContactMs, ct);
        Press("down");
        await Task.Delay(KeySettleMs, ct);
        Press("enter");
        await Task.Delay(KeySettleMs, ct);
        for (var i = 0; i < 4; i++)
        {
            Press("tab");
            await Task.Delay(TabStepMs, ct);
        }
        Press("enter");
        await Task.Delay(ConversationOpenMs, ct);

        await SendTextAsync(message, ct);
        return $"Wiadomość wysłana do {receiver} przez Instagram.";
    }

    private async Task<string> SendViaMessengerAsync(
        string url, string receiver, string message, CancellationToken ct)
    {
        if (!OpenUrl(url))
            return "Nie udało się otworzyć Messengera w przeglądarce.";

        await Task.Delay(PageLoadMs, ct);

        await SearchContactAsync(receiver, ct);
        await Task.Delay(UiSettleMs, ct);
        Press("down"); // wybór z listy podpowiedzi
        await Task.Delay(KeySettleMs, ct);
        Press("enter");
        await Task.Delay(SearchResolveMs, ct);

        await SendTextAsync(message, ct);
        return $"Wiadomość wysłana do {receiver} przez Messenger.";
    }

    /// <summary>Otwiera wyszukiwarkę (Ctrl+F), czyści pole i wpisuje nazwę odbiorcy.</summary>
    private async Task SearchContactAsync(string receiver, CancellationToken ct)
    {
        Hotkey("ctrl+f");
        await Task.Delay(UiSettleMs, ct);
        Hotkey("ctrl+a");
        await Task.Delay(ClearSelectMs, ct);
        Press("delete");
        await Task.Delay(ClearSelectMs, ct);
        InputSimulator.TypeText(receiver);
        await Task.Delay(SearchResolveMs, ct);
    }

    /// <summary>Wpisuje treść i wysyła (Enter).</summary>
    private static async Task SendTextAsync(string message, CancellationToken ct)
    {
        InputSimulator.TypeText(message);
        await Task.Delay(KeySettleMs, ct);
        Press("enter");
    }

    /// <summary>Otwiera aplikację z menu Start: Win → wpisz nazwę → Enter.</summary>
    private async Task OpenFromStartMenuAsync(string appName, CancellationToken ct)
    {
        Press("win");
        await Task.Delay(StartMenuWaitMs, ct);
        InputSimulator.TypeText(appName);
        await Task.Delay(AppNameTypedWaitMs, ct);
        Press("enter");
        await Task.Delay(AppLaunchWaitMs, ct);
    }

    private bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się otworzyć adresu {Url}.", url);
            return false;
        }
    }

    /// <summary>Spłaszcza znaki nowej linii do spacji (zapobiega przedwczesnemu „wyślij").</summary>
    private static string Flatten(string? text) => (text ?? "").ReplaceLineEndings(" ").Trim();

    private static void Press(string key) => InputSimulator.PressKey(KeyMap.ParseKey(key)!.Value);

    private static void Hotkey(string combo) => InputSimulator.Hotkey(KeyMap.ParseHotkey(combo));
}
