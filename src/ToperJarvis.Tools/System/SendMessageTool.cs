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
/// z menu Start i wyszukuje odbiorcę; komunikatory webowe (Instagram, Messenger) otwiera w
/// przeglądarce. Port <c>_Old/actions/send_message.py</c>.
/// </summary>
public sealed class SendMessageTool : IJarvisTool
{
    /// <summary>Sposób dostarczenia wiadomości dla danej platformy.</summary>
    internal enum Channel
    {
        DesktopApp,
        Browser,
    }

    /// <summary>Rozpoznana platforma: kanał + cel (nazwa aplikacji albo adres URL).</summary>
    internal readonly record struct PlatformSpec(Channel Channel, string Target);

    // Kolejność ma znaczenie — pierwsze dopasowanie wygrywa (jak w oryginale).
    private static readonly (string[] Keywords, PlatformSpec Spec)[] PlatformMap =
    [
        (["whatsapp", "wapp", "wp"], new PlatformSpec(Channel.DesktopApp, "WhatsApp")),
        (["telegram", "tg"], new PlatformSpec(Channel.DesktopApp, "Telegram")),
        (["instagram", "insta", "ig"], new PlatformSpec(Channel.Browser, "https://www.instagram.com/direct/new/")),
        (["signal"], new PlatformSpec(Channel.DesktopApp, "Signal")),
        (["discord"], new PlatformSpec(Channel.DesktopApp, "Discord")),
        (["messenger", "facebook", "fb"], new PlatformSpec(Channel.Browser, "https://www.messenger.com/")),
    ];

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
        receiver = receiver?.Trim() ?? "";
        messageText = messageText?.Trim() ?? "";
        platform = string.IsNullOrWhiteSpace(platform) ? "whatsapp" : platform.Trim();

        if (receiver.Length == 0)
            return "Podaj odbiorcę wiadomości.";
        if (messageText.Length == 0)
            return "Podaj treść wiadomości.";

        var spec = ResolvePlatform(platform);
        _logger.LogInformation("send_message: {Platform} → {Receiver}.", platform, receiver);

        try
        {
            return spec.Channel == Channel.Browser
                ? await SendViaBrowserAsync(spec.Target, receiver, messageText, platform, cancellationToken)
                : await SendViaDesktopAppAsync(spec.Target, receiver, messageText, cancellationToken);
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
        var key = platform.Trim().ToLowerInvariant();
        foreach (var (keywords, spec) in PlatformMap)
        {
            if (keywords.Any(k => key == k))
                return spec;
        }

        // Fallback: nieznana platforma = aplikacja desktopowa o tej nazwie (z wielkiej litery).
        var appName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(platform.Trim());
        return new PlatformSpec(Channel.DesktopApp, appName);
    }

    private async Task<string> SendViaDesktopAppAsync(
        string appName, string receiver, string message, CancellationToken ct)
    {
        if (!await OpenFromStartMenuAsync(appName, ct))
            return $"Nie udało się otworzyć {appName}.";

        await Task.Delay(1000, ct);

        // Wyszukaj odbiorcę (Ctrl+F → wyczyść pole → wpisz nazwę → Enter wybiera pierwszy wynik).
        Hotkey("ctrl+f");
        await Task.Delay(500, ct);
        Hotkey("ctrl+a");
        Press("delete");
        await Task.Delay(150, ct);
        InputSimulator.TypeText(receiver);
        await Task.Delay(1000, ct);
        Press("enter");
        await Task.Delay(800, ct);

        InputSimulator.TypeText(message);
        await Task.Delay(200, ct);
        Press("enter");

        return $"Wiadomość wysłana do {receiver} przez {appName}.";
    }

    private async Task<string> SendViaBrowserAsync(
        string url, string receiver, string message, string platform, CancellationToken ct)
    {
        if (!OpenUrl(url))
            return $"Nie udało się otworzyć {platform} w przeglądarce.";

        await Task.Delay(4000, ct); // czas na załadowanie strony

        // Wyszukaj/wskaż odbiorcę i otwórz konwersację.
        InputSimulator.TypeText(receiver);
        await Task.Delay(1500, ct);
        Press("down");
        await Task.Delay(300, ct);
        Press("enter");
        await Task.Delay(1500, ct);

        InputSimulator.TypeText(message);
        await Task.Delay(200, ct);
        Press("enter");

        return $"Wiadomość wysłana do {receiver} przez {platform}.";
    }

    /// <summary>Otwiera aplikację z menu Start: Win → wpisz nazwę → Enter.</summary>
    private async Task<bool> OpenFromStartMenuAsync(string appName, CancellationToken ct)
    {
        try
        {
            Press("win");
            await Task.Delay(500, ct);
            InputSimulator.TypeText(appName);
            await Task.Delay(600, ct);
            Press("enter");
            await Task.Delay(2500, ct); // czas na uruchomienie aplikacji
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się otworzyć aplikacji {App} z menu Start.", appName);
            return false;
        }
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

    private static void Press(string key) => InputSimulator.PressKey(KeyMap.ParseKey(key)!.Value);

    private static void Hotkey(string combo) => InputSimulator.Hotkey(KeyMap.ParseHotkey(combo));
}
