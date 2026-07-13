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

    private readonly float[] _context = new float[ContextSamples];
    private float[] _state = new float[2 * 128];

    // IsSpeech jest wołane ~31x/s na wątku audio — poniższe bufory/tensory są utworzone raz
    // i nadpisywane przy każdym wywołaniu (zamiast alokować od nowa), żeby zejść z GC pressure
    // na tym hot-pathcie. _state NIE jest reużywane (patrz komentarz w IsSpeech) — to jedyna
    // alokacja, która musi zostać, bo ORT-owy bufor stateN nie może być aliasowany po zwolnieniu
    // `results` (using) — musimy skopiować dane, nie referencję.
    private readonly float[] _inputBuffer = new float[ContextSamples + FrameSamples];
    private readonly DenseTensor<float> _inputTensor;
    private readonly DenseTensor<long> _srTensor = new(new[] { SampleRate }, Array.Empty<int>());

    public SileroVadModel(string modelPath, ILogger<SileroVadModel> logger)
    {
        _modelPath = modelPath;
        _logger = logger;
        _inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, _inputBuffer.Length });
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

        // Hot-path w produkcji zawsze woła z ramką dokładnie FrameSamples (patrz
        // NeuralEndpointDetector). Publiczne API dopuszcza dowolną długość, więc dla
        // nietypowego rozmiaru wracamy do bezpiecznej, alokującej ścieżki zamiast ryzykować
        // przepełnienie/niedopasowanie reużywanego bufora.
        if (frame.Length != FrameSamples)
            return IsSpeechSlow(frame, session);

        // Nadpisz reużywany bufor wejściowy (kontekst + bieżąca ramka) zamiast alokować za
        // każdym razem — IsSpeech jest wołane ~31x/s na wątku audio.
        _context.CopyTo(_inputBuffer, 0);
        frame.CopyTo(_inputBuffer.AsSpan(ContextSamples));

        // _state zmienia się co wywołanie (nowa zawartość z ORT), więc stateTensor trzeba
        // zbudować na nowo — samego bufora `_state` NIE reużywamy jako referencji do wyniku ORT
        // (patrz komentarz przy polu _state), ale opakowujący go DenseTensor jest tani (bez alokacji
        // danych, tylko wrapper).
        var stateTensor = new DenseTensor<float>(_state, new[] { 2, 1, 128 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(InputName, _inputTensor),
            NamedOnnxValue.CreateFromTensor(StateName, stateTensor),
            NamedOnnxValue.CreateFromTensor(SrName, _srTensor),
        };

        using var results = session.Run(inputs, new[] { OutputName, StateOutputName });
        var output = results.First(r => r.Name == OutputName).AsTensor<float>();
        var newState = results.First(r => r.Name == StateOutputName).AsTensor<float>();

        // Fresh copy: ORT jest właścicielem bufora newState i zwalnia go po Dispose `results`
        // (koniec using powyżej) — NIE wolno aliasować tej pamięci, trzeba ją skopiować.
        _state = newState.ToArray();

        // Zapamiętaj ostatnie 64 próbki bieżącego wejścia jako kontekst na następne wywołanie
        // (kopiujemy w miejscu do stałego bufora _context zamiast alokować nową tablicę).
        Array.Copy(_inputBuffer, _inputBuffer.Length - ContextSamples, _context, 0, ContextSamples);

        return output[0, 0];
    }

    /// <summary>Ścieżka zapasowa dla ramek o długości innej niż <see cref="FrameSamples"/> — alokuje,
    /// ale zachowuje identyczną logikę jak oryginalna (pre-optymalizacja) implementacja.</summary>
    private float IsSpeechSlow(ReadOnlySpan<float> frame, InferenceSession session)
    {
        var input = new float[ContextSamples + frame.Length];
        _context.AsSpan().CopyTo(input);
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

        var tail = input[^ContextSamples..];
        tail.CopyTo(_context, 0);

        return output[0, 0];
    }

    /// <summary>Zeruje stan LSTM oraz rolling-context. Wywołuj na starcie każdej wypowiedzi/strumienia.</summary>
    public void Reset()
    {
        Array.Clear(_context);
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
