using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.Speech.Vad;

/// <summary>
/// Detekcja aktywności głosowej oparta na energii (RMS). Buforuje próbki aż do końca wypowiedzi,
/// po czym zwraca całą wypowiedź. Używa histerezy dwóch progów, by nie ciąć w środku zdania na
/// naturalnych pauzach:
/// <list type="bullet">
/// <item>mowa zaczyna się, gdy RMS &gt; <c>SpeechThreshold</c>,</item>
/// <item>mowa kończy się dopiero, gdy RMS &lt; <c>SilenceThreshold</c> przez <c>SilenceSeconds</c>.</item>
/// </list>
/// </summary>
public sealed class VadBuffer
{
    private readonly double _speechThresh;
    private readonly double _silenceThresh;
    private readonly int _silenceSamples;
    private readonly int _minSamples;
    private readonly int _maxSamples;

    private readonly List<float> _buffer = new();
    private bool _inSpeech;
    private int _silenceCount;

    public VadBuffer(AudioOptions options)
    {
        var sr = options.SampleRate;
        _speechThresh = options.SpeechThreshold;
        _silenceThresh = options.SilenceThreshold;
        _silenceSamples = (int)(options.SilenceSeconds * sr);
        _minSamples = (int)(options.MinSpeechSeconds * sr);
        _maxSamples = (int)(options.MaxSpeechSeconds * sr);
    }

    /// <summary>
    /// Podaje jedną ramkę audio (mono float32). Zwraca kompletną wypowiedź, gdy mowa się kończy,
    /// w przeciwnym razie <c>null</c>.
    /// </summary>
    public float[]? Process(ReadOnlySpan<float> chunk)
    {
        var rms = Rms(chunk);

        if (rms > _speechThresh)
        {
            _inSpeech = true;
            _silenceCount = 0;
            Append(chunk);
        }
        else if (_inSpeech)
        {
            Append(chunk);
            if (rms < _silenceThresh)
                _silenceCount += chunk.Length;

            if (_silenceCount >= _silenceSamples || _buffer.Count >= _maxSamples)
            {
                var audio = _buffer.ToArray();
                Reset();
                return audio.Length >= _minSamples ? audio : null;
            }
        }

        return null;
    }

    /// <summary>Czyści stan bufora (np. po wyciszeniu mikrofonu lub powrocie do stanu Idle).</summary>
    public void Reset()
    {
        _buffer.Clear();
        _inSpeech = false;
        _silenceCount = 0;
    }

    private void Append(ReadOnlySpan<float> chunk)
    {
        foreach (var s in chunk)
            _buffer.Add(s);
    }

    private static double Rms(ReadOnlySpan<float> chunk)
    {
        if (chunk.Length == 0)
            return 0.0;

        double sum = 0.0;
        foreach (var s in chunk)
            sum += (double)s * s;

        return Math.Sqrt(sum / chunk.Length);
    }
}
