using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Speech.Vad;

namespace ToperJarvis.Speech.Endpointing;

/// <summary>Tworzy nowy detektor końca wypowiedzi (jedna instancja na turę — stanowy bufor).</summary>
public interface IEndpointDetectorFactory
{
    IEndpointDetector Create();
}

/// <summary>
/// Wybiera silnik endpointingu wg <see cref="AudioOptions.EndpointEngine"/>: "smartturn" —
/// neuronowy detektor (Silero VAD + Smart Turn) owijający współdzielone singletony modeli;
/// w przeciwnym razie (w tym "rms") — energetyczny <see cref="VadBuffer"/>.
/// </summary>
public sealed class EndpointDetectorFactory : IEndpointDetectorFactory
{
    private readonly JarvisOptions _options;
    private readonly SileroVadModel _vad;
    private readonly SmartTurnModel _turn;
    private readonly ILogger<EndpointDetectorFactory> _logger;

    public EndpointDetectorFactory(
        IOptions<JarvisOptions> options,
        SileroVadModel vad,
        SmartTurnModel turn,
        ILogger<EndpointDetectorFactory> logger)
    {
        _options = options.Value;
        _vad = vad;
        _turn = turn;
        _logger = logger;
    }

    public IEndpointDetector Create()
    {
        if (_options.Audio.EndpointEngine.Trim().ToLowerInvariant() != "smartturn")
            return new VadBuffer(_options.Audio);

        // Brak pliku modelu Silero VAD → SileroVadModel.IsSpeech degraduje na stałe do 1.0 (zawsze
        // "mowa"), więc endpointing NIGDY nie wykryje ciszy i Smart Turn nigdy nie zostanie odpytany —
        // turę kończy wtedy wyłącznie MaxTurnSeconds (30 s), co czyni asystenta bezużytecznym.
        // Bezpieczniej spaść na energetyczny VadBuffer i głośno to zalogować, niż ciche 30-sekundowe
        // zawieszenie każdej wypowiedzi.
        if (!File.Exists(_options.SmartTurn.SileroVadPath))
        {
            _logger.LogError(
                "EndpointEngine=smartturn, ale brak modelu Silero VAD pod ścieżką {Path}. " +
                "Fallback na VadBuffer (rms) — bez tego endpointing nigdy nie wykryłby ciszy i każda " +
                "tura trwałaby do MaxTurnSeconds.",
                _options.SmartTurn.SileroVadPath);
            return new VadBuffer(_options.Audio);
        }

        // SileroVadModel jest singletonem niosącym stan LSTM między turami — każda nowa tura
        // musi zaczynać z czystym stanem VAD (bufor samego NeuralEndpointDetector jest już świeży).
        _vad.Reset();

        return new NeuralEndpointDetector(
            isSpeech: m => _vad.IsSpeech(m.Span),
            predictCompletion: m => _turn.PredictCompletion(m.Span),
            resetVad: _vad.Reset,
            opts: _options.SmartTurn,
            sampleRate: _options.Audio.SampleRate);
    }
}
