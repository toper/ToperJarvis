using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.Tts;

/// <summary>
/// Dekorator <see cref="ITextToSpeech"/> cache'ujący syntezowane PCM po znormalizowanym tekście.
/// <para>
/// Polityka cache: pamiętamy WYŁĄCZNIE frazy z <see cref="TtsOptions.FillerPhrases"/> (znormalizowane).
/// To jedyny przypadek, gdzie ta sama fraza pada wielokrotnie i gdzie liczy się natychmiastowość
/// (odtwarzanie podczas oczekiwania na odpowiedź LLM). Dowolne inne zdania (odpowiedzi LLM) są za
/// każdym razem syntezowane na nowo i NIE trafiają do cache — są w praktyce unikalne, a ich
/// cache'owanie prowadziłoby do nieograniczonego wzrostu pamięci.
/// </para>
/// </summary>
public sealed class CachingTextToSpeech : ITextToSpeech
{
    private readonly IPcmSynthesizer _synthesizer;
    private readonly IAudioOutput _output;
    private readonly TtsOptions _options;
    private readonly ILogger<CachingTextToSpeech> _logger;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();
    private readonly HashSet<string> _cacheableKeys;

    public CachingTextToSpeech(
        IPcmSynthesizer synthesizer,
        IAudioOutput output,
        TtsOptions options,
        ILogger<CachingTextToSpeech> logger)
    {
        _synthesizer = synthesizer;
        _output = output;
        _options = options;
        _logger = logger;
        _cacheableKeys = new HashSet<string>(
            options.FillerPhrases.Select(NormalizeKey),
            StringComparer.Ordinal);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var key = NormalizeKey(text);
        var cacheable = _options.CacheEnabled && _cacheableKeys.Contains(key);

        byte[] pcm;
        if (cacheable && _cache.TryGetValue(key, out var cached))
        {
            pcm = cached;
        }
        else
        {
            pcm = await _synthesizer.SynthesizeToPcmAsync(text, ct);
            if (cacheable && pcm.Length > 0)
                _cache[key] = pcm;
        }

        if (pcm.Length == 0)
            return;

        var player = new RawPcmPlayer(_output.DeviceNumber, _synthesizer.SampleRate);
        using var stream = new MemoryStream(pcm);
        await player.PlayAsync(stream, ct);
    }

    /// <summary>Wstępnie syntezuje i cache'uje wszystkie frazy filler, by pierwsze użycie było natychmiastowe.</summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        if (!_options.CacheEnabled)
            return;

        foreach (var phrase in _options.FillerPhrases)
        {
            var key = NormalizeKey(phrase);
            if (_cache.ContainsKey(key))
                continue;

            var pcm = await _synthesizer.SynthesizeToPcmAsync(phrase, ct);
            if (pcm.Length > 0)
                _cache[key] = pcm;
            else
                _logger.LogWarning("Nie udało się wstępnie zsyntezować frazy filler: {Phrase}", phrase);
        }
    }

    private static string NormalizeKey(string text) => text.Trim().ToLowerInvariant();
}
