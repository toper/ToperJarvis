using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class SileroVadModelTests
{
    // Ścieżka rozwiązywana względem korzenia repo (nie cwd testhosta, który wskazuje
    // na bin/Debug/net10.0), żeby test faktycznie uruchamiał realną inferencję ONNX.
    private static readonly string ModelPath =
        Path.Combine(FindRepoRoot(), "assets", "silero", "silero_vad.onnx");

    [Fact]
    public void Brak_modelu_degraduje_do_mowy()
    {
        using var m = new SileroVadModel("nie/istnieje.onnx", NullLogger<SileroVadModel>.Instance);
        var prob = m.IsSpeech(new float[512]);
        Assert.Equal(1.0f, prob);
    }

    [Fact]
    public void Reset_nie_rzuca()
    {
        using var m = new SileroVadModel("nie/istnieje.onnx", NullLogger<SileroVadModel>.Instance);
        m.Reset();
    }

    [Fact]
    public void Cisza_daje_niskie_prawdopodobienstwo_mowy()
    {
        if (!File.Exists(ModelPath)) return; // pominięcie tylko gdy model naprawdę niedostępny (świeże CI)
        using var m = new SileroVadModel(ModelPath, NullLogger<SileroVadModel>.Instance);
        var prob = m.IsSpeech(new float[512]); // 32 ms ciszy
        Assert.InRange(prob, 0f, 1f);
        Assert.True(prob < 0.5f, $"Oczekiwano niskiego prawdopodobieństwa mowy dla ciszy, otrzymano {prob}.");
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
