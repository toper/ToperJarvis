using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class NeuralEndpointDetectorTests
{
    private const int SampleRate = 16000;
    private const int FrameSamples = 512; // ramka Silero

    // Fake VAD oparty na treści ramki: ramka jest "mową", gdy jej pierwsza próbka != 0.
    // Pozwala kodować tożsamość próbek (marker-wartości) i śledzić, które trafiają gdzie.
    private static float ByContent(ReadOnlyMemory<float> frame) =>
        frame.Span.Length > 0 && frame.Span[0] != 0f ? 1.0f : 0.0f;

    private static float[] Filled(int count, float value)
    {
        var a = new float[count];
        Array.Fill(a, value);
        return a;
    }

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
        // VadSilenceSeconds = 3 ramki, żeby reset licznika ciszy po werdykcie "low completion"
        // był ODRÓŻNIALNY od jego braku (przy 1 ramce następna ramka ciszy i tak przekroczyłaby próg).
        var opts = new SmartTurnOptions
        {
            CompletionThreshold = 0.5f,
            VadSilenceSeconds = (double)(FrameSamples * 3) / SampleRate, // 3 ramki ciszy
            MaxTurnSeconds = 100.0,
        };

        var speech = true;
        var completionCalls = 0;
        var detector = new NeuralEndpointDetector(
            isSpeech: _ => speech ? 1.0f : 0.0f,
            predictCompletion: _ =>
            {
                completionCalls++;
                return 0.0f; // < progu → tylko pauza
            },
            resetVad: () => { },
            opts: opts,
            sampleRate: SampleRate);

        Assert.Null(detector.Process(new float[FrameSamples])); // mowa
        speech = false;

        // 2 ramki ciszy — jeszcze poniżej progu 3 ramek, Smart Turn nie pytany.
        Assert.Null(detector.Process(new float[FrameSamples]));
        Assert.Null(detector.Process(new float[FrameSamples]));
        Assert.Equal(0, completionCalls);

        // 3. ramka ciszy osiąga próg → Smart Turn pytany raz, werdykt low → licznik ciszy wyzerowany.
        Assert.Null(detector.Process(new float[FrameSamples]));
        Assert.Equal(1, completionCalls);

        // Kluczowy dowód resetu: kolejna POJEDYNCZA ramka ciszy NIE wywołuje Smart Turn ponownie
        // (gdyby licznik nie był zerowany, wciąż byłby >= próg i pytałby co ramkę).
        Assert.Null(detector.Process(new float[FrameSamples]));
        Assert.Equal(1, completionCalls);

        // Dopiero po kolejnych pełnych 3 ramkach ciszy Smart Turn jest pytany drugi raz.
        Assert.Null(detector.Process(new float[FrameSamples]));
        Assert.Null(detector.Process(new float[FrameSamples]));
        Assert.Equal(2, completionCalls);
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
    public void Koniec_tury_w_srodku_chunku_zachowuje_ogon()
    {
        // Turn-end na 1. ramce chunku 1600-próbkowego → próbki z ramek 2-3 (ogon) muszą przetrwać
        // Reset() i zasiać początek kolejnej wypowiedzi, a nie zginąć.
        var opts = new SmartTurnOptions
        {
            CompletionThreshold = 0.5f,
            VadSilenceSeconds = (double)FrameSamples / SampleRate, // 1 ramka ciszy kończy
            MaxTurnSeconds = 100.0,
        };

        const float marker = 7.0f; // znacznik próbek z ogona
        var detector = new NeuralEndpointDetector(
            isSpeech: ByContent,
            predictCompletion: _ => 1.0f,
            resetVad: () => { },
            opts: opts,
            sampleRate: SampleRate);

        // Zainicjuj mowę (1 ramka wartości 1.0), żeby wejść w stan _inSpeech.
        Assert.Null(detector.Process(Filled(FrameSamples, 1.0f)));

        // Chunk 1600 próbek: 1. ramka = cisza (zera), reszta (1088 próbek) = marker (mowa).
        var mixed = new float[1600];
        Array.Fill(mixed, marker, FrameSamples, mixed.Length - FrameSamples); // 512..1599 = marker

        var utterance1 = detector.Process(mixed);

        // 1. wypowiedź = mowa startowa (512) + ramka ciszy (512), BEZ próbek markera z ogona.
        Assert.NotNull(utterance1);
        Assert.Equal(FrameSamples * 2, utterance1!.Length);
        Assert.DoesNotContain(marker, utterance1);

        // Ogon (1088 próbek markera) powinien zostać zachowany jako początek kolejnej wypowiedzi.
        // Domknij drugą turę ciszą i sprawdź, że WSZYSTKIE 1088 próbek markera się w niej znalazły.
        var utterance2 = detector.Process(new float[1600]); // sama cisza → domyka 2. turę
        Assert.NotNull(utterance2);
        var preserved = utterance2!.Count(x => x == marker);
        Assert.Equal(1088, preserved); // 1600 - 512 = 1088 próbek ogona, nic nie zgubione
    }

    [Fact]
    public void Reset_czysci_stan()
    {
        var opts = new SmartTurnOptions
        {
            CompletionThreshold = 0.5f,
            VadSilenceSeconds = (double)FrameSamples / SampleRate, // 1 ramka ciszy
            MaxTurnSeconds = 100.0,
        };

        const float oldMarker = 3.0f;
        const float newMarker = 5.0f;
        var resetVadCalled = false;
        float[]? captured = null;

        var detector = new NeuralEndpointDetector(
            isSpeech: ByContent,
            predictCompletion: mem => { captured = mem.ToArray(); return 1.0f; },
            resetVad: () => resetVadCalled = true,
            opts: opts,
            sampleRate: SampleRate);

        // Zbuforuj starą mowę chunkiem NIEwyrównanym do ramki (600) — 512 do bufora, 88 do reszty (_carry).
        Assert.Null(detector.Process(Filled(600, oldMarker)));

        detector.Reset();
        Assert.True(resetVadCalled);
        Assert.Null(captured); // Reset nie kończy tury, więc Smart Turn nie był pytany

        // Nowa tura: mowa (nowy marker), potem cisza domykająca. Bufor przekazany do Smart Turn
        // (i zwrócony) NIE może zawierać starych próbek — dowód, że _buffer ORAZ _carry są czyste.
        Assert.Null(detector.Process(Filled(FrameSamples, newMarker)));
        var result = detector.Process(new float[FrameSamples]); // cisza → koniec tury

        Assert.NotNull(result);
        Assert.NotNull(captured);
        Assert.DoesNotContain(oldMarker, captured!); // stary bufor i ogon _carry zniknęły
        Assert.Contains(newMarker, captured!);
        Assert.Equal(result, captured); // zwrócona wypowiedź = migawka przekazana do Smart Turn
    }
}
