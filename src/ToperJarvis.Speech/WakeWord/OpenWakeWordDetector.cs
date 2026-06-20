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
    private readonly IAudioCapture _capture;
    private readonly ILogger<OpenWakeWordDetector> _logger;
    private readonly WakeWordOptions _options;

    private WakeWordRuntime? _runtime;
    private bool _running;

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

        _runtime = new WakeWordRuntime(new WakeWordRuntimeConfig
        {
            WakeWords = new[]
            {
                new WakeWordConfig { Model = _options.Model, Threshold = _options.Sensitivity },
            },
        });

        _capture.FrameAvailable += OnFrame;
        _running = true;
        _logger.LogInformation("openWakeWord uruchomiony (model: {Model}).", _options.Model);
    }

    public void Stop()
    {
        if (!_running)
            return;

        _capture.FrameAvailable -= OnFrame;
        _runtime = null;
        _running = false;
    }

    private void OnFrame(object? sender, AudioFrame frame)
    {
        var runtime = _runtime;
        if (runtime is null)
            return;

        var pcm = ToPcm16(frame.Samples);
        if (runtime.Process(pcm) >= 0)
        {
            _logger.LogInformation("Wykryto słowo-klucz (openWakeWord).");
            Detected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Konwertuje próbki float (-1..1) na 16-bit PCM z przycięciem zakresu.</summary>
    internal static short[] ToPcm16(ReadOnlySpan<float> samples)
    {
        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (int)(samples[i] * 32767f);
            pcm[i] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
        }
        return pcm;
    }

    public void Dispose() => Stop();
}
