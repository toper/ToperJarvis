using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pv;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.WakeWord;

/// <summary>
/// Detektor słowa-klucza oparty na Picovoice Porcupine (wbudowane słowo „Jarvis").
/// Konsumuje strumień z <see cref="IAudioCapture"/> i buforuje próbki do ramek o długości
/// <see cref="Porcupine.FrameLength"/> (16-bit PCM) wymaganych przez silnik.
/// </summary>
public sealed class PorcupineWakeWordDetector : IWakeWordDetector
{
    private readonly IAudioCapture _capture;
    private readonly ILogger<PorcupineWakeWordDetector> _logger;
    private readonly WakeWordOptions _options;

    private Porcupine? _porcupine;
    private short[] _frame = Array.Empty<short>();
    private int _frameFill;
    private bool _running;

    public PorcupineWakeWordDetector(
        IAudioCapture capture,
        IOptions<JarvisOptions> options,
        ILogger<PorcupineWakeWordDetector> logger)
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

        if (string.IsNullOrWhiteSpace(_options.AccessKey))
        {
            _logger.LogWarning("Brak AccessKey Porcupine — wykrywanie słowa-klucza wyłączone. " +
                               "Uzupełnij Jarvis:WakeWord:AccessKey w appsettings.Local.json.");
            return;
        }

        var keyword = ParseKeyword(_options.Keyword);
        _porcupine = Porcupine.FromBuiltInKeywords(
            _options.AccessKey,
            new[] { keyword },
            sensitivities: new[] { _options.Sensitivity });

        _frame = new short[_porcupine.FrameLength];
        _frameFill = 0;
        _capture.FrameAvailable += OnFrame;
        _running = true;
        _logger.LogInformation("Porcupine uruchomiony (słowo-klucz: {Keyword}).", _options.Keyword);
    }

    public void Stop()
    {
        if (!_running)
            return;

        _capture.FrameAvailable -= OnFrame;
        _porcupine?.Dispose();
        _porcupine = null;
        _frameFill = 0;
        _running = false;
    }

    private void OnFrame(object? sender, AudioFrame frame)
    {
        var porcupine = _porcupine;
        if (porcupine is null)
            return;

        foreach (var sample in frame.Samples)
        {
            // float (-1..1) → 16-bit PCM
            var s = (int)(sample * 32767f);
            _frame[_frameFill++] = (short)Math.Clamp(s, short.MinValue, short.MaxValue);

            if (_frameFill < _frame.Length)
                continue;

            _frameFill = 0;
            if (porcupine.Process(_frame) >= 0)
            {
                _logger.LogInformation("Wykryto słowo-klucz: {Keyword}.", _options.Keyword);
                Detected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private static BuiltInKeyword ParseKeyword(string keyword) =>
        Enum.TryParse<BuiltInKeyword>(keyword.Replace(" ", "_"), ignoreCase: true, out var parsed)
            ? parsed
            : BuiltInKeyword.JARVIS;

    public void Dispose() => Stop();
}
