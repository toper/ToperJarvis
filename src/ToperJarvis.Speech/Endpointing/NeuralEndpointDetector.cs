using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.Speech.Endpointing;

/// <summary>
/// Neuronowy endpointing: łączy Silero VAD (detekcja mowy per ramka 512 próbek) ze Smart Turn
/// (predykcja, czy użytkownik skończył wypowiedź). Bufor wypowiedzi rośnie odkąd Silero wykryje
/// mowę; gdy Silero pokaże ciszę przez <see cref="SmartTurnOptions.VadSilenceSeconds"/>, pytamy
/// Smart Turn o completion — wysoki wynik kończy turę, niski oznacza tylko pauzę (dalszy nasłuch).
/// Bezpiecznik <see cref="SmartTurnOptions.MaxTurnSeconds"/> wymusza koniec niezależnie od Smart Turn.
/// </summary>
/// <remarks>
/// Sondy VAD/Smart Turn są wstrzykiwane jako delegaty (nie konkretne klasy modeli), żeby dało się
/// testować logikę progów/stanu bez ładowania ONNX. W produkcji owijają
/// <see cref="SileroVadModel.IsSpeech"/> / <see cref="SmartTurnModel.PredictCompletion"/> /
/// <see cref="SileroVadModel.Reset"/> (patrz fabryka w Task 1.8).
/// </remarks>
public sealed class NeuralEndpointDetector : IEndpointDetector
{
    // Silero pracuje na ramkach dokładnie 512 próbek @16 kHz; wejściowe porcje audio (~100 ms /
    // 1600 próbek) są cięte na takie ramki, z resztą przenoszoną do kolejnego wywołania Process.
    private const int FrameSamples = 512;

    // Silero zwraca prawdopodobieństwo mowy już bez sigmoidu — 0.5 to naturalny punkt odcięcia
    // decydujący, czy dana ramka to mowa czy cisza.
    private const float VadSpeechThreshold = 0.5f;

    private readonly Func<ReadOnlyMemory<float>, float> _isSpeech;
    private readonly Func<ReadOnlyMemory<float>, float> _predictCompletion;
    private readonly Action _resetVad;
    private readonly SmartTurnOptions _opts;
    private readonly int _silenceSamples;
    private readonly int _maxSamples;

    private readonly List<float> _carry = new();
    private readonly List<float> _buffer = new();
    private bool _inSpeech;
    private int _silenceCount;

    public NeuralEndpointDetector(
        Func<ReadOnlyMemory<float>, float> isSpeech,
        Func<ReadOnlyMemory<float>, float> predictCompletion,
        Action resetVad,
        SmartTurnOptions opts,
        int sampleRate)
    {
        _isSpeech = isSpeech;
        _predictCompletion = predictCompletion;
        _resetVad = resetVad;
        _opts = opts;
        _silenceSamples = (int)(opts.VadSilenceSeconds * sampleRate);
        _maxSamples = (int)(opts.MaxTurnSeconds * sampleRate);
    }

    /// <summary>
    /// Podaje porcję audio (mono float32 @16 kHz, dowolnej długości — typowo ~100 ms). Zwraca
    /// kompletną wypowiedź, gdy tura się kończy (Smart Turn albo bezpiecznik czasowy), w
    /// przeciwnym razie <c>null</c>.
    /// </summary>
    public float[]? Process(ReadOnlySpan<float> chunk)
    {
        _carry.AddRange(chunk.ToArray());

        var offset = 0;
        while (_carry.Count - offset >= FrameSamples)
        {
            var frame = _carry.GetRange(offset, FrameSamples).ToArray();
            offset += FrameSamples;

            // Zachowaj niezużyty ogon bieżącego chunku (próbki PO tej ramce) ZANIM ProcessFrame
            // ewentualnie zakończy turę i wywoła Reset() — te próbki należą już do początku
            // kolejnej wypowiedzi i nie mogą zginąć (barge-in, Faza 4).
            var tail = _carry.Count > offset
                ? _carry.GetRange(offset, _carry.Count - offset).ToArray()
                : Array.Empty<float>();

            var result = ProcessFrame(frame);
            if (result is not null)
            {
                // ProcessFrame wywołał Reset() (koniec tury) — _carry jest wyczyszczony.
                // Ponownie zasiej go niezużytym ogonem, by początek następnej tury nie zginął.
                if (tail.Length > 0)
                    _carry.AddRange(tail);
                return result;
            }
        }

        if (offset > 0)
            _carry.RemoveRange(0, offset);

        return null;
    }

    /// <summary>Zeruje bufor wypowiedzi, licznik ciszy, resztę ramki i stan Silero (przez <c>resetVad</c>).</summary>
    public void Reset()
    {
        _buffer.Clear();
        _carry.Clear();
        _inSpeech = false;
        _silenceCount = 0;
        _resetVad();
    }

    private float[]? ProcessFrame(float[] frame)
    {
        var prob = _isSpeech(frame);
        var speech = prob >= VadSpeechThreshold;

        if (speech)
        {
            _inSpeech = true;
            _silenceCount = 0;
            _buffer.AddRange(frame);
        }
        else if (_inSpeech)
        {
            // Cisza po rozpoczęciu mowy — dokładamy do bufora (Smart Turn potrzebuje kontekstu
            // pauzy) i liczymy czas ciszy.
            _buffer.AddRange(frame);
            _silenceCount += frame.Length;
        }
        else
        {
            // Cisza przed jakąkolwiek mową — nic do zbuforowania.
            return null;
        }

        // Bezpiecznik czasowy ma priorytet nad pytaniem Smart Turn.
        if (_buffer.Count >= _maxSamples)
        {
            var forced = _buffer.ToArray();
            Reset();
            return forced;
        }

        if (_silenceCount >= _silenceSamples)
        {
            var snapshot = _buffer.ToArray();
            var completion = _predictCompletion(snapshot);
            if (completion >= _opts.CompletionThreshold)
            {
                Reset();
                return snapshot;
            }

            // Użytkownik tylko zrobił pauzę — kontynuuj nasłuch, wyzeruj licznik ciszy, żeby
            // znów odczekać pełne VadSilenceSeconds przed kolejnym pytaniem Smart Turn.
            _silenceCount = 0;
        }

        return null;
    }
}
