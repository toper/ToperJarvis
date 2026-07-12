using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ToperJarvis.Speech.Endpointing;

/// <summary>
/// Wrapper ONNX dla Silero VAD — przewiduje prawdopodobieństwo mowy dla ramki 512
/// próbek @16 kHz. Model przyjmuje surowe PCM float32 (bez log-mela) jako tensor
/// <c>input</c> [1, N] (tu N=576 po doklejeniu 64-próbkowego kontekstu), stan LSTM
/// <c>state</c> [2, 1, 128] oraz <c>sr</c> (int64 scalar = 16000). Zwraca <c>output</c>
/// [1, 1] — prawdopodobieństwo mowy JUŻ w [0,1] (bez sigmoidu) — oraz <c>stateN</c>
/// [2, 1, 128], nowy stan do przekazania w kolejnym wywołaniu.
/// </summary>
public sealed class SileroVadModel : IDisposable
{
    private const int FrameSamples = 512;
    private const int ContextSamples = 64;
    private const long SampleRate = 16000;
    private const string InputName = "input";
    private const string StateName = "state";
    private const string SrName = "sr";
    private const string OutputName = "output";
    private const string StateOutputName = "stateN";

    private readonly string _modelPath;
    private readonly ILogger<SileroVadModel> _logger;
    private readonly object _gate = new();

    private InferenceSession? _session;
    private bool _missingModelWarned;

    private float[] _context = new float[ContextSamples];
    private float[] _state = new float[2 * 128];

    public SileroVadModel(string modelPath, ILogger<SileroVadModel> logger)
    {
        _modelPath = modelPath;
        _logger = logger;
    }

    /// <summary>
    /// Zwraca prawdopodobieństwo [0..1], że ramka 512 próbek @16 kHz zawiera mowę.
    /// Brak modelu → degradacja do 1.0f (traktuj wszystko jak mowę, żeby endpointing
    /// spadł na próg czasu Smart Turn).
    /// </summary>
    public float IsSpeech(ReadOnlySpan<float> frame)
    {
        var session = EnsureSession();
        if (session is null)
            return 1.0f;

        // Doklej ostatnie 64 próbki poprzedniego wywołania przed bieżącą ramką → 576.
        var input = new float[ContextSamples + frame.Length];
        _context.CopyTo(input, 0);
        frame.CopyTo(input.AsSpan(ContextSamples));

        var inputTensor = new DenseTensor<float>(input, new[] { 1, input.Length });
        var stateTensor = new DenseTensor<float>(_state, new[] { 2, 1, 128 });
        var srTensor = new DenseTensor<long>(new[] { SampleRate }, Array.Empty<int>());

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputName, inputTensor),
            NamedOnnxValue.CreateFromTensor(StateName, stateTensor),
            NamedOnnxValue.CreateFromTensor(SrName, srTensor),
        };

        using var results = session.Run(inputs, new[] { OutputName, StateOutputName });
        var output = results.First(r => r.Name == OutputName).AsTensor<float>();
        var newState = results.First(r => r.Name == StateOutputName).AsTensor<float>();

        _state = newState.ToArray();

        // Zapamiętaj ostatnie 64 próbki bieżącego wejścia jako kontekst na następne wywołanie.
        _context = input[^ContextSamples..];

        return output[0, 0];
    }

    /// <summary>Zeruje stan LSTM oraz rolling-context. Wywołuj na starcie każdej wypowiedzi/strumienia.</summary>
    public void Reset()
    {
        _context = new float[ContextSamples];
        _state = new float[2 * 128];
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
                        "Brak modelu Silero VAD pod ścieżką {Path} — VAD degraduje do 'mowa'.",
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
                _logger.LogInformation("Silero VAD załadowany ({Model}).", _modelPath);
                return _session;
            }
            catch (Exception ex)
            {
                if (!_missingModelWarned)
                {
                    _logger.LogWarning(
                        ex,
                        "Nie udało się załadować modelu Silero VAD z {Path} — VAD degraduje do 'mowa'.",
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
