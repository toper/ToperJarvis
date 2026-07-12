using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class NeuralEndpointDetectorTests
{
    private const int SampleRate = 16000;
    private const int FrameSamples = 512; // ramka Silero

    [Fact]
    public void Mowa_potem_cisza_z_wysokim_completion_konczy_ture()
    {
        var opts = new SmartTurnOptions
        {
            CompletionThreshold = 0.5f,
            VadSilenceSeconds = (double)FrameSamples / SampleRate, // dokładnie 1 ramka ciszy
            MaxTurnSeconds = 100.0,
        };

        var speech = true; // przełącznik: fake VAD zwraca mowę, potem ciszę
        var detector = new NeuralEndpointDetector(
            isSpeech: _ => speech ? 1.0f : 0.0f,
            predictCompletion: _ => 1.0f, // >= progu → koniec tury
            resetVad: () => { },
            opts: opts,
            sampleRate: SampleRate);

        // Ramka mowy — dalej nasłuch.
        var r1 = detector.Process(new float[FrameSamples]);
        Assert.Null(r1);

        // Przełącz na ciszę — dokładnie 1 ramka ciszy osiąga VadSilenceSeconds.
        speech = false;
        var r2 = detector.Process(new float[FrameSamples]);

        Assert.NotNull(r2);
        Assert.Equal(FrameSamples * 2, r2!.Length); // bufor = ramka mowy + ramka ciszy
    }

    [Fact]
    public void Pauza_z_niskim_completion_nie_konczy_tury()
    {
        var opts = new SmartTurnOptions
        {
            CompletionThreshold = 0.5f,
            VadSilenceSeconds = (double)FrameSamples / SampleRate,
            MaxTurnSeconds = 100.0,
        };

        var speech = true;
        var detector = new NeuralEndpointDetector(
            isSpeech: _ => speech ? 1.0f : 0.0f,
            predictCompletion: _ => 0.0f, // < progu → tylko pauza
            resetVad: () => { },
            opts: opts,
            sampleRate: SampleRate);

        var r1 = detector.Process(new float[FrameSamples]); // mowa
        Assert.Null(r1);

        speech = false;
        var r2 = detector.Process(new float[FrameSamples]); // cisza osiąga próg, ale completion niski
        Assert.Null(r2);

        // Kolejna cisza — licznik ciszy wyzerowany po nieudanym completion, więc znowu
        // potrzeba pełnego VadSilenceSeconds zanim znów spytamy Smart Turn; nadal null.
        var r3 = detector.Process(new float[FrameSamples]);
        Assert.Null(r3);
    }

    [Fact]
    public void MaxTurnSeconds_wymusza_koniec()
    {
        var opts = new SmartTurnOptions
        {
            CompletionThreshold = 0.5f,
            VadSilenceSeconds = 10.0, // nieosiągalne w tym teście
            MaxTurnSeconds = (double)(FrameSamples * 2) / SampleRate, // 2 ramki
        };

        var completionCalled = false;
        var detector = new NeuralEndpointDetector(
            isSpeech: _ => 1.0f, // ciągła mowa, bez ciszy
            predictCompletion: _ => { completionCalled = true; return 0.0f; },
            resetVad: () => { },
            opts: opts,
            sampleRate: SampleRate);

        var r1 = detector.Process(new float[FrameSamples]);
        Assert.Null(r1);

        var r2 = detector.Process(new float[FrameSamples]); // bufor osiąga MaxTurnSeconds
        Assert.NotNull(r2);
        Assert.Equal(FrameSamples * 2, r2!.Length);
        Assert.False(completionCalled, "Bezpiecznik MaxTurnSeconds nie powinien pytać Smart Turn.");
    }

    [Fact]
    public void Reset_czysci_stan()
    {
        var opts = new SmartTurnOptions
        {
            CompletionThreshold = 0.5f,
            VadSilenceSeconds = (double)FrameSamples / SampleRate,
            MaxTurnSeconds = 100.0,
        };

        var resetVadCalled = false;
        var speech = true;
        var detector2 = new NeuralEndpointDetector(
            isSpeech: _ => speech ? 1.0f : 0.0f,
            predictCompletion: _ => 1.0f,
            resetVad: () => resetVadCalled = true,
            opts: opts,
            sampleRate: SampleRate);

        var before = detector2.Process(new float[FrameSamples]); // bufor = 1 ramka mowy
        Assert.Null(before);

        detector2.Reset();
        Assert.True(resetVadCalled);

        // Po Reset, cisza (bez uprzedniej mowy) nie powinna nic zwrócić — potwierdza,
        // że stan _inSpeech/bufor/licznik ciszy zostały wyczyszczone.
        speech = false;
        var after = detector2.Process(new float[FrameSamples]);
        Assert.Null(after);
    }
}
