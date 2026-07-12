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
/// Synteza mowy offline przez Piper. Utrzymuje JEDEN trwały proces <c>piper.exe --json-input</c>,
/// dzięki czemu model głosu (~63 MB) ładuje się raz, a kolejne zdania syntezują się w kilkadziesiąt
/// ms (zamiast ~400 ms narzutu na proces przy każdym zdaniu). Piper zapisuje WAV per zdanie i
/// wypisuje jego ścieżkę na stdout — to sygnał końca syntezy. Odtwarzanie przez NAudio.
/// </summary>
public sealed class PiperTextToSpeech : ITextToSpeech, IPcmSynthesizer, IDisposable
{
    private readonly TtsOptions _options;
    private readonly IAudioOutput _output;
    private readonly ILogger<PiperTextToSpeech> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _tempDir;
    private readonly System.Text.StringBuilder _stderr = new();

    private Process? _piper;
    private int _counter;
    private int? _actualSampleRate;

    public PiperTextToSpeech(IOptions<JarvisOptions> options, IAudioOutput output, ILogger<PiperTextToSpeech> logger)
    {
        _options = options.Value.Tts;
        _output = output;
        _logger = logger;
        _tempDir = Path.Combine(Path.GetTempPath(), "ToperJarvis", "tts");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Częstotliwość próbkowania modelu głosu. Po pierwszej udanej syntezie zwraca RZECZYWISTY sample
    /// rate odczytany z wygenerowanego WAV (patrz <see cref="SynthesizeToWavAsync"/>/<see cref="PlayAsync"/>)
    /// — chroni przed rozjazdem z <see cref="TtsOptions.SampleRate"/>, gdyby konfiguracja nie zgadzała
    /// się z modelem (inaczej odtwarzanie z cache — <see cref="CachingTextToSpeech"/> — grałoby ze złą
    /// prędkością). Przed pierwszą syntezą zwraca wartość skonfigurowaną.
    /// </summary>
    public int SampleRate => _actualSampleRate ?? _options.SampleRate;

    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _gate.WaitAsync(ct);
        try
        {
            var outPath = await SynthesizeToWavAsync(text, ct);
            if (outPath is null)
                return;

            if (File.Exists(outPath))
            {
                await PlayAsync(outPath, ct);
                TryDelete(outPath);
            }
            else
            {
                _logger.LogWarning("Piper nie utworzył pliku {Path}. stderr: {Err}", outPath, RecentStderr());
            }
        }
        catch (OperationCanceledException)
        {
            // Przerwanie mowy (Esc/nowa komenda). Jeśli anulowano po wysłaniu tekstu, a przed odczytem
            // ścieżki WAV, proces Pipera ma niedoczytaną linię na stdout → rozjazd parowania
            // żądanie↔odpowiedź. Reset gwarantuje czysty proces dla następnego zdania.
            ResetProcess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd syntezy Piper — restart procesu.");
            ResetProcess();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Syntetyzuje tekst do bufora PCM (16-bit mono), bez odtwarzania. Używane przez cache
    /// (<c>CachingTextToSpeech</c>) do wstępnej syntezy fraz filler. Brak Pipera/pliku → pusty bufor
    /// (degradacja spójna z <see cref="SpeakAsync"/>).
    /// </summary>
    public async Task<byte[]> SynthesizeToPcmAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<byte>();

        await _gate.WaitAsync(ct);
        try
        {
            var outPath = await SynthesizeToWavAsync(text, ct);
            if (outPath is null || !File.Exists(outPath))
                return Array.Empty<byte>();

            try
            {
                using var reader = new WaveFileReader(outPath);
                _actualSampleRate ??= reader.WaveFormat.SampleRate;
                var pcm = new byte[reader.Length];
                var read = 0;
                while (read < pcm.Length)
                {
                    var n = await reader.ReadAsync(pcm.AsMemory(read), ct);
                    if (n <= 0)
                        break;
                    read += n;
                }
                return read == pcm.Length ? pcm : pcm[..read];
            }
            finally
            {
                TryDelete(outPath);
            }
        }
        catch (OperationCanceledException)
        {
            // Przerwanie syntezy (Esc/nowa komenda), jak w SpeakAsync: proces Pipera mógł zostać
            // z niedoczytaną linią na stdout → rozjazd parowania żądanie↔odpowiedź. Reset zapewnia
            // czysty proces dla następnego wywołania. To normalne zakończenie, nie błąd — brak logu Error.
            ResetProcess();
            return Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd syntezy Piper do PCM — restart procesu.");
            ResetProcess();
            return Array.Empty<byte>();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Wspólny rdzeń syntezy: wysyła zdanie do trwałego procesu Pipera przez stdin (protokół
    /// <c>--json-input</c>) i czyta ścieżkę gotowego pliku WAV ze stdout. Współdzielony przez
    /// <see cref="SpeakAsync"/> (synteza+odtwarzanie) i <see cref="SynthesizeToPcmAsync"/> (synteza→PCM),
    /// by nie duplikować protokołu stdin/stdout Pipera. Wywołujący trzyma <c>_gate</c>.
    /// </summary>
    private async Task<string?> SynthesizeToWavAsync(string text, CancellationToken ct)
    {
        var piper = EnsureProcess();
        if (piper is null)
            return null;

        var outPath = Path.Combine(_tempDir, $"seg_{Interlocked.Increment(ref _counter)}.wav");
        var line = JsonSerializer.Serialize(new { text, output_file = outPath });

        await piper.StandardInput.WriteLineAsync(line.AsMemory(), ct);
        await piper.StandardInput.FlushAsync(ct);

        // Piper wypisuje ścieżkę gotowego pliku WAV na stdout. Pomijamy ewentualne inne linie
        // (banner/log), by nie rozjechać parowania żądanie↔odpowiedź; limit chroni przed zawisem.
        string? donePath = null;
        for (var i = 0; i < 8; i++)
        {
            var l = await piper.StandardOutput.ReadLineAsync(ct);
            if (l is null)
                break; // proces padł
            if (l.Trim().EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                donePath = l.Trim();
                break;
            }
        }

        if (donePath is null)
        {
            _logger.LogWarning("Piper nie zwrócił ścieżki audio (proces padł?). stderr: {Err}", RecentStderr());
            ResetProcess();
            return null;
        }

        return outPath;
    }

    /// <summary>Uruchamia (raz) trwały proces piper.exe w trybie json-input. Null = brak plików.</summary>
    private Process? EnsureProcess()
    {
        if (_piper is { HasExited: false })
            return _piper;

        if (!File.Exists(_options.PiperPath) || !File.Exists(_options.VoiceModelPath))
        {
            _logger.LogWarning("Brak piper.exe ({Piper}) lub modelu głosu ({Voice}) — TTS wyłączone.",
                _options.PiperPath, _options.VoiceModelPath);
            return null;
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
            // Katalog roboczy = katalog Pipera (espeak-ng-data leży obok exe).
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_options.PiperPath)) ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(_options.VoiceModelPath);
        psi.ArgumentList.Add("--json-input");
        psi.ArgumentList.Add("--length_scale");
        psi.ArgumentList.Add(lengthScale.ToString(CultureInfo.InvariantCulture));

        var process = new Process { StartInfo = psi };
        process.Start();

        // Drenaż stderr w tle + zachowanie ostatnich linii do diagnostyki (inaczej pełny bufor blokuje proces).
        _ = Task.Run(async () =>
        {
            try
            {
                string? l;
                while ((l = await process.StandardError.ReadLineAsync()) is not null)
                {
                    lock (_stderr)
                    {
                        _stderr.AppendLine(l);
                        if (_stderr.Length > 2000)
                            _stderr.Remove(0, _stderr.Length - 2000);
                    }
                }
            }
            catch { /* proces zakończony */ }
        });

        _piper = process;
        _logger.LogInformation("Piper uruchomiony (trwały proces, model: {Voice}).", _options.VoiceModelPath);
        return _piper;
    }

    private string RecentStderr()
    {
        lock (_stderr)
            return _stderr.Length == 0 ? "(brak)" : _stderr.ToString().Trim();
    }

    private void ResetProcess()
    {
        try { _piper?.Kill(true); } catch { /* ignoruj */ }
        _piper?.Dispose();
        _piper = null;
    }

    private async Task PlayAsync(string wavPath, CancellationToken ct)
    {
        using var reader = new WaveFileReader(wavPath);
        _actualSampleRate ??= reader.WaveFormat.SampleRate;
        using var output = new WaveOutEvent { DeviceNumber = _output.DeviceNumber };
        var tcs = new TaskCompletionSource();

        output.PlaybackStopped += (_, _) => tcs.TrySetResult();
        output.Init(reader);
        output.Play();

        using (ct.Register(() =>
        {
            try { output.Stop(); } catch { /* ignoruj */ }
            tcs.TrySetCanceled();
        }))
        {
            await tcs.Task;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* plik zniknie z TEMP */ }
    }

    public void Dispose()
    {
        ResetProcess();
        _gate.Dispose();
    }
}
