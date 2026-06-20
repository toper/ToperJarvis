using ToperJarvis.Speech.WakeWord;

namespace ToperJarvis.Core.Tests.Speech;

public class OpenWakeWordDetectorTests
{
    [Fact]
    public void ToPcm16_skaluje_i_przycina()
    {
        var input = new[] { 0f, 1f, -1f, 0.5f, 2f, -2f };
        var pcm = new short[input.Length];
        OpenWakeWordDetector.ToPcm16(input, pcm);

        Assert.Equal(0, pcm[0]);
        Assert.Equal(32767, pcm[1]);   // 1.0 → max
        Assert.Equal(-32767, pcm[2]);  // -1.0
        Assert.Equal(16383, pcm[3]);   // 0.5 → ~połowa
        Assert.Equal(32767, pcm[4]);   // 2.0 przycięte do max
        Assert.Equal(-32768, pcm[5]);  // -2.0 przycięte do min
    }

    [Fact]
    public void ToPcm16_pusta_tablica()
    {
        var ex = Record.Exception(() =>
            OpenWakeWordDetector.ToPcm16(Array.Empty<float>(), Array.Empty<short>()));

        Assert.Null(ex);
    }
}
