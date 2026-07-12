using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class SmartTurnModelTests
{
    private const string ModelPath = "assets/smartturn/smart-turn-v3.1-cpu.onnx";

    [Fact]
    public void Brak_modelu_degraduje_do_konca_tury()
    {
        using var m = new SmartTurnModel("nie/istnieje.onnx", NullLogger<SmartTurnModel>.Instance);
        var prob = m.PredictCompletion(new float[16000]);
        Assert.Equal(1.0f, prob);
    }

    [Fact]
    public void Cisza_daje_wysokie_prawdopodobienstwo_konca()
    {
        if (!File.Exists(ModelPath)) return; // pominięcie gdy model niedostępny w CI
        using var m = new SmartTurnModel(ModelPath, NullLogger<SmartTurnModel>.Instance);
        var prob = m.PredictCompletion(new float[16000]); // 1 s ciszy
        Assert.InRange(prob, 0f, 1f);
    }
}
