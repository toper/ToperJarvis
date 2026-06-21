using ToperJarvis.Tools.Vision;

namespace ToperJarvis.Tools.Tests;

public class FfmpegTests
{
    [Fact]
    public void PcmBytesToFloats_konwertuje_float32_little_endian()
    {
        // 1.0f i -0.5f jako bajty little-endian.
        var bytes = new byte[8];
        BitConverter.GetBytes(1.0f).CopyTo(bytes, 0);
        BitConverter.GetBytes(-0.5f).CopyTo(bytes, 4);

        var samples = Ffmpeg.PcmBytesToFloats(bytes);

        Assert.Equal(2, samples.Length);
        Assert.Equal(1.0f, samples[0]);
        Assert.Equal(-0.5f, samples[1]);
    }

    [Fact]
    public void PcmBytesToFloats_ucina_niepelny_ogon()
    {
        // 6 bajtów = 1 pełna próbka (4 B) + 2 bajty ogona.
        var samples = Ffmpeg.PcmBytesToFloats(new byte[6]);

        Assert.Single(samples);
    }

    [Fact]
    public void PcmBytesToFloats_pusty_daje_pusta_tablice()
    {
        Assert.Empty(Ffmpeg.PcmBytesToFloats([]));
    }
}
