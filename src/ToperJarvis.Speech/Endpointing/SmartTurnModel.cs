using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ToperJarvis.Speech.Endpointing;

/// <summary>
/// Wrapper ONNX dla Smart Turn v3.1 — przewiduje prawdopodobieństwo, że użytkownik
/// skończył wypowiedź (koniec tury). Model przyjmuje log-mel spektrogram 8 s audio
/// (16 kHz) jako tensor <c>input_features</c> [1, 80, 800] i zwraca w
/// <c>logits</c> [1, 1] wartość JUŻ po sigmoidzie (prawdopodobieństwo, nie logit).
/// </summary>
public sealed class SmartTurnModel : IDisposable
{
    private const int SampleRate = 16000;
    private const int WindowSeconds = 8;
    private const int WindowSamples = SampleRate * WindowSeconds; // 128000
    private const string InputName = "input_features";
    private const string OutputName = "logits";

    private readonly string _modelPath;
    private readonly ILogger<SmartTurnModel> _logger;
    private readonly object _gate = new();

    private InferenceSession? _session;
    private bool _missingModelWarned;

    public SmartTurnModel(string modelPath, ILogger<SmartTurnModel> logger)
    {
        _modelPath = modelPath;
        _logger = logger;
    }

    /// <summary>
    /// Zwraca prawdopodobieństwo [0..1], że użytkownik skończył turę. Brak modelu →
    /// degradacja do 1.0f (zachowuj się jak „koniec”, żeby nie zawiesić pipeline'u).
    /// </summary>
    public float PredictCompletion(ReadOnlySpan<float> pcm16k)
    {
        var session = EnsureSession();
        if (session is null)
            return 1.0f;

        var windowed = PadOrTrim(pcm16k);
        var mel = WhisperMelSpectrogram.Compute(windowed);

        var inputTensor = new DenseTensor<float>(mel, new[] { 1, 80, 800 });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputName, inputTensor),
        };

        using var results = session.Run(inputs, new[] { OutputName });
        var output = results.First(r => r.Name == OutputName).AsTensor<float>();
        return output[0, 0];
    }

    /// <summary>Padding/truncation do dokładnie 8 s (128000 próbek): zera z przodu, audio na końcu.</summary>
    private static float[] PadOrTrim(ReadOnlySpan<float> pcm)
    {
        if (pcm.Length == WindowSamples)
            return pcm.ToArray();

        var result = new float[WindowSamples];
        if (pcm.Length < WindowSamples)
        {
            pcm.CopyTo(result.AsSpan(WindowSamples - pcm.Length));
        }
        else
        {
            pcm[^WindowSamples..].CopyTo(result);
        }

        return result;
    }

    private InferenceSession? EnsureSession()
    {
        lock (_gate)
        {
            if (_session is not null)
                return _session;

            if (!File.Exists(_modelPath))
            {
                if (!_missingModelWarned)
                {
                    _logger.LogWarning(
                        "Brak modelu Smart Turn pod ścieżką {Path} — endpointing degraduje do 'koniec tury'.",
                        _modelPath);
                    _missingModelWarned = true;
                }

                return null;
            }

            try
            {
                var options = new SessionOptions
                {
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    InterOpNumThreads = 1,
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                };

                _session = new InferenceSession(_modelPath, options);
                _logger.LogInformation("Smart Turn załadowany ({Model}).", _modelPath);
                return _session;
            }
            catch (Exception ex)
            {
                if (!_missingModelWarned)
                {
                    _logger.LogWarning(
                        ex,
                        "Nie udało się załadować modelu Smart Turn z {Path} — endpointing degraduje do 'koniec tury'.",
                        _modelPath);
                    _missingModelWarned = true;
                }

                return null;
            }
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
