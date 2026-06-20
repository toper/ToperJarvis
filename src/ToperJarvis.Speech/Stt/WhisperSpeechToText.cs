using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using Whisper.net;

namespace ToperJarvis.Speech.Stt;

/// <summary>
/// Rozpoznawanie mowy offline przez Whisper.net (whisper.cpp). Model ggml ładowany leniwie
/// z <see cref="SttOptions.ModelPath"/>; fabryka i procesor współdzielone między wywołaniami.
/// </summary>
public sealed class WhisperSpeechToText : ISpeechToText, IDisposable
{
    private readonly SttOptions _options;
    private readonly ILogger<WhisperSpeechToText> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    public WhisperSpeechToText(IOptions<JarvisOptions> options, ILogger<WhisperSpeechToText> logger)
    {
        _options = options.Value.Stt;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var processor = EnsureProcessor();
            if (processor is null)
                return string.Empty;

            var sb = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, ct))
                sb.Append(segment.Text);

            return sb.ToString().Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd transkrypcji Whisper.");
            return string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    private WhisperProcessor? EnsureProcessor()
    {
        if (_processor is not null)
            return _processor;

        if (!File.Exists(_options.ModelPath))
        {
            _logger.LogWarning("Brak modelu Whisper pod ścieżką {Path} — STT wyłączone.", _options.ModelPath);
            return null;
        }

        _factory = WhisperFactory.FromPath(_options.ModelPath);
        var builder = _factory.CreateBuilder();
        builder = string.Equals(_options.Language, "auto", StringComparison.OrdinalIgnoreCase)
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(_options.Language);

        _processor = builder.Build();
        _logger.LogInformation("Whisper załadowany ({Model}, język: {Lang}).", _options.ModelPath, _options.Language);
        return _processor;
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
        _gate.Dispose();
    }
}
