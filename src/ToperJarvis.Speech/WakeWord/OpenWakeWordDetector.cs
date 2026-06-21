using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NanoWakeWord;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.WakeWord;

/// <summary>
/// Detektor słowa-klucza oparty na open-source openWakeWord (port NanoWakeWord, ONNX Runtime).
/// Modele (melspectrogram/embedding/hey_jarvis) są wbudowane w pakiet — nie wymaga klucza ani
/// pobierania plików. Konsumuje strumień z <see cref="IAudioCapture"/>.
/// </summary>
public sealed class OpenWakeWordDetector : IWakeWordDetector
{
    /// <summary>openWakeWord operuje na 16 kHz mono — inny sample-rate daje błędne mel-spektrogramy.</summary>
    private const int RequiredSampleRate = 16000;

    private readonly IAudioCapture _capture;
    private readonly ILogger<OpenWakeWordDetector> _logger;
    private readonly WakeWordOptions _options;

    private WakeWordRuntime? _runtime;
    private short[] _pcm = Array.Empty<short>();
    private bool _running;

    // Diagnostyka score'u wake-worda.
    private int _scoreFrames;
    private float _peakScore;

    public OpenWakeWordDetector(
        IAudioCapture capture,
        IOptions<JarvisOptions> options,
        ILogger<OpenWakeWordDetector> logger)
    {
        _capture = capture;
        _logger = logger;
        _options = options.Value.WakeWord;
    }

    public event EventHandler? Detected;

    public void Start()
    {
        if (_running)
            return;

        if (_capture.SampleRate != RequiredSampleRate)
        {
            _logger.LogWarning(
                "Niezgodny sample-rate audio: capture={Capture} Hz, openWakeWord wymaga {Required} Hz — " +
                "wykrywanie słowa-klucza wyłączone.", _capture.SampleRate, RequiredSampleRate);
            return;
        }

        try
        {
            // NanoWakeWord ładuje modele ONNX z katalogu „models/" (względem bieżącego katalogu).
            // Wbudowane modele trzeba najpierw wypakować — bez tego konstruktor runtime rzuca
            // „models/melspectrogram.onnx doesn't exist" i wykrywanie byłoby cicho wyłączone.
            WakeWordUtil.ExtractModels(typeof(WakeWordRuntime).Assembly, false);

            _runtime = new WakeWordRuntime(new WakeWordRuntimeConfig
            {
                WakeWords = new[]
                {
                    new WakeWordConfig
                    {
                        Model = _options.Model,
                        Threshold = ToThreshold(_options.Sensitivity),
                        TriggerLevel = _options.TriggerLevel,
                    },
                },
                // Diagnostyka: śledzi szczytowy score i loguje go okresowo (zawsze), a detekcję
                // natychmiast — pozwala potwierdzić, jak wysoko dochodzi score przy „Hey Jarvis".
                DebugAction = OnWakeScore,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Nie udało się zainicjować openWakeWord (model: {Model}) — wykrywanie słowa-klucza wyłączone.",
                _options.Model);
            _runtime = null;
            return;
        }

        _capture.FrameAvailable += OnFrame;
        _running = true;
        _logger.LogInformation("openWakeWord uruchomiony (model: {Model}, próg {Threshold:F2}, TriggerLevel {Trigger}).",
            _options.Model, ToThreshold(_options.Sensitivity), _options.TriggerLevel);
    }

    public void Stop()
    {
        if (!_running)
            return;

        _capture.FrameAvailable -= OnFrame;
        _runtime?.Dispose();
        _runtime = null;
        _running = false;
    }

    private void OnFrame(object? sender, AudioFrame frame)
    {
        var runtime = _runtime;
        if (runtime is null)
            return;

        var samples = frame.Samples;
        if (_pcm.Length != samples.Length)
            _pcm = new short[samples.Length];

        ToPcm16(samples, _pcm);
        if (runtime.Process(_pcm) >= 0)
        {
            _logger.LogInformation("Wykryto słowo-klucz (openWakeWord).");
            Detected?.Invoke(this, EventArgs.Empty);
        }
    }

    // Wywoływane przez NanoWakeWord dla każdej predykcji. Loguje szczyt co ~2 s + detekcję od razu.
    private void OnWakeScore(string model, float score, bool detected)
    {
        if (score > _peakScore)
            _peakScore = score;

        if (detected)
            _logger.LogInformation("Wake-word [{Model}] score {Score:F2} ← DETEKCJA.", model, score);

        if (++_scoreFrames < 24)
            return;

        _logger.LogInformation("Wake-word [{Model}] szczyt score (~2 s): {Peak:F3} (próg {Threshold:F2}).",
            model, _peakScore, ToThreshold(_options.Sensitivity));
        _scoreFrames = 0;
        _peakScore = 0f;
    }

    /// <summary>Konwertuje próbki float (-1..1) na 16-bit PCM z przycięciem zakresu, zapisując do <paramref name="pcm"/>.</summary>
    internal static void ToPcm16(ReadOnlySpan<float> samples, Span<short> pcm)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (int)(samples[i] * 32767f);
            pcm[i] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
        }
    }

    /// <summary>
    /// Mapuje czułość 0..1 (wyższa = więcej detekcji) na próg openWakeWord, który ma odwrotną
    /// polaryzację (wyższy próg = mniej detekcji). Przy 0.5 zwraca 0.5 (zalecany domyślny próg),
    /// dzięki czemu kontrakt „wyższa czułość = więcej detekcji" jest spójny z silnikiem Porcupine.
    /// </summary>
    internal static float ToThreshold(float sensitivity) => Math.Clamp(1f - sensitivity, 0f, 1f);

    public void Dispose() => Stop();
}
