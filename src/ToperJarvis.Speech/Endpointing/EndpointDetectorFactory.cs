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

    public EndpointDetectorFactory(IOptions<JarvisOptions> options, SileroVadModel vad, SmartTurnModel turn)
    {
        _options = options.Value;
        _vad = vad;
        _turn = turn;
    }

    public IEndpointDetector Create()
    {
        if (_options.Audio.EndpointEngine.Trim().ToLowerInvariant() != "smartturn")
            return new VadBuffer(_options.Audio);

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
