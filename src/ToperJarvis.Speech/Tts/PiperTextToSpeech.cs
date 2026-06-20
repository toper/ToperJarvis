using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.Tts;

/// <summary>
/// Synteza mowy offline przez Piper (proces <c>piper.exe</c>). Tekst trafia na stdin, a surowy
/// 16-bit PCM (mono) wraca na stdout i jest odtwarzany przez NAudio. Sample rate odczytywany
/// z pliku konfiguracji głosu (<c>&lt;model&gt;.onnx.json</c>).
/// </summary>
public sealed class PiperTextToSpeech : ITextToSpeech
{
    private readonly TtsOptions _options;
    private readonly ILogger<PiperTextToSpeech> _logger;
    private readonly int _sampleRate;

    public PiperTextToSpeech(IOptions<JarvisOptions> options, ILogger<PiperTextToSpeech> logger)
    {
        _options = options.Value.Tts;
        _logger = logger;
        _sampleRate = ReadSampleRate(_options.VoiceModelPath);
    }

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!File.Exists(_options.PiperPath) || !File.Exists(_options.VoiceModelPath))
        {
            _logger.LogWarning("Brak piper.exe ({Piper}) lub modelu głosu ({Voice}) — TTS wyłączone.",
                _options.PiperPath, _options.VoiceModelPath);
            return;
        }

        var lengthScale = _options.Speed > 0 ? 1.0 / _options.Speed : 1.0;
        var psi = new ProcessStartInfo
        {
            FileName = _options.PiperPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(_options.VoiceModelPath);
        psi.ArgumentList.Add("--output-raw");
        psi.ArgumentList.Add("--length-scale");
        psi.ArgumentList.Add(lengthScale.ToString(CultureInfo.InvariantCulture));

        using var process = new Process { StartInfo = psi };
        process.Start();

        await process.StandardInput.WriteLineAsync(text);
        process.StandardInput.Close();

        using var pcm = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(pcm, ct);
        await process.WaitForExitAsync(ct);

        if (pcm.Length == 0)
        {
            var err = await process.StandardError.ReadToEndAsync(ct);
            _logger.LogWarning("Piper nie zwrócił audio. stderr: {Err}", err);
            return;
        }

        pcm.Position = 0;
        await PlayAsync(pcm, ct);
    }

    private async Task PlayAsync(Stream pcm, CancellationToken ct)
    {
        var format = new WaveFormat(_sampleRate, 16, 1);
        await using var reader = new RawSourceWaveStream(pcm, format);
        using var output = new WaveOutEvent();
        var tcs = new TaskCompletionSource();

        output.PlaybackStopped += (_, _) => tcs.TrySetResult();
        output.Init(reader);
        output.Play();

        await using (ct.Register(() =>
        {
            output.Stop();
            tcs.TrySetCanceled();
        }))
        {
            await tcs.Task;
        }
    }

    private int ReadSampleRate(string voiceModelPath)
    {
        const int defaultRate = 22050;
        try
        {
            var configPath = voiceModelPath + ".json";
            if (!File.Exists(configPath))
                return defaultRate;

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("sample_rate", out var sr))
                return sr.GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Nie udało się odczytać sample_rate głosu — używam {Rate}.", defaultRate);
        }

        return defaultRate;
    }
}
