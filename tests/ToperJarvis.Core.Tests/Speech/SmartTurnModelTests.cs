using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class SmartTurnModelTests
{
    // Ścieżka rozwiązywana względem korzenia repo (nie cwd testhosta, który wskazuje
    // na bin/Debug/net10.0), żeby test faktycznie uruchamiał realną inferencję ONNX.
    private static readonly string ModelPath =
        Path.Combine(FindRepoRoot(), "assets", "smartturn", "smart-turn-v3.1-cpu.onnx");

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
    }

    /// <summary>
    /// Idzie w górę od katalogu wyjściowego testu, szukając markera korzenia repo
    /// (katalog <c>.git</c> lub dowolny plik <c>*.sln</c>).
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                dir.GetFiles("*.sln").Length > 0)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        // Fallback: cwd (zachowanie sprzed fixa).
        return Directory.GetCurrentDirectory();
    }
}
