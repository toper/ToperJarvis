using NAudio.Wave;

namespace ToperJarvis.Speech.Tts;

/// <summary>
/// Odtwarza surowy strumień PCM (16-bit mono) w miarę jego napływania, bez czekania na cały plik WAV.
/// Używane przez <see cref="CachingTextToSpeech"/> do odtwarzania zcache'owanego PCM (frazy filler).
/// Streamingowe TTS "w locie" prosto z Pipera (Task 2.3) zostało pominięte (patrz decyzja w
/// docs/superpowers/notes/piper-streaming-decision.md) — dominującym kosztem okazał się reload modelu,
/// nie brak strumieniowania.
/// Bezpieczne bez urządzenia audio (np. CI/headless): błąd inicjalizacji WaveOutEvent jest połykany.
/// </summary>
public sealed class RawPcmPlayer
{
    private const int ChunkBytes = 3200;

    private readonly int _deviceNumber;
    private readonly int _sampleRate;

    public RawPcmPlayer(int deviceNumber, int sampleRate)
    {
        _deviceNumber = deviceNumber;
        _sampleRate = sampleRate;
    }

    public async Task PlayAsync(Stream pcmStream, CancellationToken ct)
    {
        var bufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(_sampleRate, 16, 1))
        {
            DiscardOnBufferOverflow = false,
            BufferDuration = TimeSpan.FromSeconds(30),
        };

        WaveOutEvent? output = null;
        var tcs = new TaskCompletionSource();
        var started = false;

        using var registration = ct.Register(() =>
        {
            try { output?.Stop(); } catch { /* ignoruj */ }
            tcs.TrySetCanceled(ct);
        });

        try
        {
            var buffer = new byte[ChunkBytes];
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var read = await pcmStream.ReadAsync(buffer, ct);
                if (read <= 0)
                    break;

                bufferedWaveProvider.AddSamples(buffer, 0, read);

                if (!started)
                {
                    started = true;
                    output = TryStartPlayback(bufferedWaveProvider, tcs);
                    if (output is null)
                        return; // brak urządzenia audio — kończymy bez błędu (środowisko headless/CI)
                }
            }

            // Stream wyczerpany — czekaj aż bufor się dogra.
            if (output is not null)
            {
                while (bufferedWaveProvider.BufferedBytes > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(20, ct);
                }

                output.Stop();
            }
            else
            {
                tcs.TrySetResult();
            }

            await tcs.Task;
        }
        finally
        {
            output?.Dispose();
        }
    }

    /// <summary>Próbuje wystartować odtwarzanie; zwraca null gdy brak urządzenia (nie propaguje wyjątku).</summary>
    private WaveOutEvent? TryStartPlayback(BufferedWaveProvider provider, TaskCompletionSource tcs)
    {
        WaveOutEvent output;
        try
        {
            output = new WaveOutEvent { DeviceNumber = _deviceNumber };
            output.PlaybackStopped += (_, _) => tcs.TrySetResult();
            output.Init(provider);
            output.Play();
        }
        catch
        {
            // Brak wyjścia audio (headless/CI) — kończymy odtwarzanie bez błędu.
            tcs.TrySetResult();
            return null;
        }

        return output;
    }
}
