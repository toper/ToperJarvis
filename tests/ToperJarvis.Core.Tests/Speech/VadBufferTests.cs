using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Speech.Vad;

namespace ToperJarvis.Core.Tests.Speech;

public class VadBufferTests
{
    private const int SampleRate = 16000;
    private const int ChunkSize = 1600; // 0.1 s

    private static AudioOptions Options() => new()
    {
        SampleRate = SampleRate,
        SpeechThreshold = 0.008,
        SilenceThreshold = 0.004,
        SilenceSeconds = 0.7,      // 11200 próbek = 7 ramek
        MinSpeechSeconds = 0.3,    // 4800 próbek = 3 ramki
        MaxSpeechSeconds = 30.0,
    };

    private static float[] SpeechChunk() => Filled(0.05f);   // RMS 0.05 > próg mowy
    private static float[] SilenceChunk() => Filled(0.0f);   // RMS 0   < próg ciszy

    private static float[] Filled(float value)
    {
        var chunk = new float[ChunkSize];
        Array.Fill(chunk, value);
        return chunk;
    }

    [Fact]
    public void Sama_cisza_nie_zwraca_wypowiedzi()
    {
        var vad = new VadBuffer(Options());

        for (var i = 0; i < 20; i++)
            Assert.Null(vad.Process(SilenceChunk()));
    }

    [Fact]
    public void Mowa_a_potem_cisza_zwraca_wypowiedz()
    {
        var vad = new VadBuffer(Options());

        // 5 ramek mowy (8000 próbek > min)
        for (var i = 0; i < 5; i++)
            Assert.Null(vad.Process(SpeechChunk()));

        // cisza krótsza niż próg — wciąż null
        for (var i = 0; i < 6; i++)
            Assert.Null(vad.Process(SilenceChunk()));

        // 7. ramka ciszy domyka wypowiedź (silenceCount >= 11200)
        var utterance = vad.Process(SilenceChunk());

        Assert.NotNull(utterance);
        Assert.True(utterance!.Length >= (int)(0.3 * SampleRate));
    }

    [Fact]
    public void Zbyt_krotka_mowa_jest_odrzucana()
    {
        var vad = new VadBuffer(Options());

        // tylko 1 ramka mowy (1600 próbek < min 4800)
        Assert.Null(vad.Process(SpeechChunk()));

        // cisza domykająca — ale wypowiedź za krótka → null
        for (var i = 0; i < 7; i++)
            vad.Process(SilenceChunk());

        // kolejna cisza nie powinna nic zwrócić (bufor już zresetowany)
        Assert.Null(vad.Process(SilenceChunk()));
    }

    [Fact]
    public void Reset_czysci_stan_bufora()
    {
        var vad = new VadBuffer(Options());

        for (var i = 0; i < 5; i++)
            vad.Process(SpeechChunk());

        vad.Reset();

        // po resecie sama cisza nie domknie żadnej wypowiedzi
        for (var i = 0; i < 10; i++)
            Assert.Null(vad.Process(SilenceChunk()));
    }
}
