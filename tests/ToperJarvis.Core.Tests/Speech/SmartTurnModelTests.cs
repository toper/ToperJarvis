using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class SmartTurnModelTests
{
    // Ścieżka rozwiązywana względem korzenia repo (nie cwd testhosta, który wskazuje
    // na bin/Debug/net10.0), żeby test faktycznie uruchamiał realną inferencję ONNX.
    private static readonly string ModelPath =
        Path.Combine(TestRepoRoot.Find(), "assets", "smartturn", "smart-turn-v3.1-cpu.onnx");

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
        if (!File.Exists(ModelPath)) return; // pominięcie tylko gdy model naprawdę niedostępny (świeże CI)
        using var m = new SmartTurnModel(ModelPath, NullLogger<SmartTurnModel>.Instance);
        var prob = m.PredictCompletion(new float[16000]); // 1 s ciszy
        Assert.InRange(prob, 0f, 1f);
        // Cisza = tura skończona — model powinien zwrócić WYSOKIE prawdopodobieństwo konca
        // (realnie ~0.979), nie tylko dowolną wartość z [0,1].
        Assert.True(prob > 0.5f, $"Oczekiwano wysokiego prawdopodobieństwa konca tury dla ciszy, otrzymano {prob}.");
    }
}
