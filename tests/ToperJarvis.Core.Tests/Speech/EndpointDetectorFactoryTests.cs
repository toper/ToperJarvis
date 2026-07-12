using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Speech.Endpointing;
using ToperJarvis.Speech.Vad;

namespace ToperJarvis.Core.Tests.Speech;

public class EndpointDetectorFactoryTests
{
    private static EndpointDetectorFactory CreateFactory(string engine, string sileroVadPath = "nieistniejacy/silero.onnx")
    {
        var options = Options.Create(new JarvisOptions
        {
            Audio = new AudioOptions { EndpointEngine = engine, SampleRate = 16000 },
            SmartTurn = new SmartTurnOptions
            {
                ModelPath = "nieistniejacy/smart-turn.onnx",
                SileroVadPath = sileroVadPath,
            },
        });

        var vad = new SileroVadModel(options.Value.SmartTurn.SileroVadPath, NullLogger<SileroVadModel>.Instance);
        var turn = new SmartTurnModel(options.Value.SmartTurn.ModelPath, NullLogger<SmartTurnModel>.Instance);

        return new EndpointDetectorFactory(options, vad, turn, NullLogger<EndpointDetectorFactory>.Instance);
    }

    [Fact]
    public void Fabryka_tworzy_VadBuffer_dla_rms()
    {
        var factory = CreateFactory("rms");

        var detector = factory.Create();

        Assert.IsType<VadBuffer>(detector);
    }

    [Fact]
    public void Fabryka_tworzy_NeuralEndpointDetector_dla_smartturn_gdy_model_silero_istnieje()
    {
        // Ścieżka realnego assetu modelu Silero — jeśli katalog assets nie jest wdrożony w środowisku
        // testowym, ten test i tak weryfikuje fallback (patrz test poniżej dla brakującego pliku);
        // tu podmieniamy File.Exists pośrednio przez wskazanie istniejącego pliku tymczasowego.
        var tempSilero = Path.GetTempFileName();
        try
        {
            var factory = CreateFactory("smartturn", tempSilero);

            var detector = factory.Create();

            Assert.IsType<NeuralEndpointDetector>(detector);
        }
        finally
        {
            File.Delete(tempSilero);
        }
    }

    [Fact]
    public void Smartturn_z_brakujacym_silero_spada_na_VadBuffer_zamiast_zawieszac_ture()
    {
        // Brak pliku Silero VAD → IsSpeech zawsze zwróciłoby 1.0 (zawsze "mowa"), więc endpointing
        // nigdy nie wykryłby ciszy i Smart Turn nigdy nie zostałby odpytany — turę kończyłby
        // wyłącznie MaxTurnSeconds (30 s). Fabryka musi w tej sytuacji spaść na VadBuffer.
        var factory = CreateFactory("smartturn", "nieistniejacy/silero.onnx");

        var detector = factory.Create();

        Assert.IsType<VadBuffer>(detector);
    }

    [Fact]
    public void Smartturn_bez_modeli_nadal_zwraca_dzialajacy_detektor()
    {
        var factory = CreateFactory("smartturn");

        var detector = factory.Create();

        var chunk = new float[1600];
        var exception = Record.Exception(() => detector.Process(chunk));

        Assert.Null(exception);
    }
}
