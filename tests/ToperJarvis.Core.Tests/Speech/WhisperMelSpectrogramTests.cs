using System.Text.Json;
using ToperJarvis.Speech.Endpointing;

namespace ToperJarvis.Core.Tests.Speech;

public class WhisperMelSpectrogramTests
{
    private sealed record Golden(float[] Pcm, float[] Mel, int NMels, int NFft, int Hop);

    [Fact]
    public void Mel_zgodny_ze_zlotym_wektorem_referencyjnym()
    {
        var json = File.ReadAllText("Fixtures/mel_golden.json");
        var g = JsonSerializer.Deserialize<Golden>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var mel = WhisperMelSpectrogram.Compute(g.Pcm, g.NMels, g.NFft, g.Hop);

        Assert.Equal(g.Mel.Length, mel.Length);
        for (var i = 0; i < mel.Length; i++)
            Assert.True(Math.Abs(mel[i] - g.Mel[i]) < 1e-2f,
                $"bin {i}: {mel[i]} vs {g.Mel[i]}");
    }
}
