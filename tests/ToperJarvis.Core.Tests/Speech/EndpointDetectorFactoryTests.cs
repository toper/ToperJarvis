using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Speech.Endpointing;
using ToperJarvis.Speech.Vad;

namespace ToperJarvis.Core.Tests.Speech;

public class EndpointDetectorFactoryTests
{
    private static EndpointDetectorFactory CreateFactory(string engine)
    {
        var options = Options.Create(new JarvisOptions
        {
            Audio = new AudioOptions { EndpointEngine = engine, SampleRate = 16000 },
            SmartTurn = new SmartTurnOptions
            {
                ModelPath = "nieistniejacy/smart-turn.onnx",
                SileroVadPath = "nieistniejacy/silero.onnx",
            },
        });

        var vad = new SileroVadModel(options.Value.SmartTurn.SileroVadPath, NullLogger<SileroVadModel>.Instance);
        var turn = new SmartTurnModel(options.Value.SmartTurn.ModelPath, NullLogger<SmartTurnModel>.Instance);

        return new EndpointDetectorFactory(options, vad, turn);
    }

    [Theory]
    [InlineData("rms")]
    [InlineData("smartturn")]
    public void Fabryka_tworzy_detektor_wg_configu(string engine)
    {
        var factory = CreateFactory(engine);

        var detector = factory.Create();

        Assert.IsAssignableFrom<IEndpointDetector>(detector);
        if (engine == "rms")
            Assert.IsType<VadBuffer>(detector);
        else
            Assert.IsType<NeuralEndpointDetector>(detector);
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
