using Microsoft.Extensions.Options;
using NAudio.Wave;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.Audio;

/// <summary>
/// Przechwytywanie audio z domyślnego mikrofonu przez NAudio (<see cref="WaveInEvent"/>).
/// Strumień: mono, 16-bit PCM → konwertowany do float32 (-1..1) i emitowany ramkami co ~100 ms.
/// </summary>
public sealed class NAudioCapture : IAudioCapture
{
    private readonly int _sampleRate;
    private WaveInEvent? _waveIn;

    public NAudioCapture(IOptions<JarvisOptions> options)
    {
        _sampleRate = options.Value.Audio.SampleRate;
    }

    public int SampleRate => _sampleRate;

    public event EventHandler<AudioFrame>? FrameAvailable;

    public void Start()
    {
        if (_waveIn is not null)
            return;

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(_sampleRate, 16, 1),
            BufferMilliseconds = 100,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    public void Stop()
    {
        if (_waveIn is null)
            return;

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var handler = FrameAvailable;
        if (handler is null)
            return;

        var sampleCount = e.BytesRecorded / 2; // 16-bit = 2 bajty/próbkę
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
            samples[i] = sample / 32768f;
        }

        handler(this, new AudioFrame(samples, _sampleRate));
    }

    public void Dispose() => Stop();
}
