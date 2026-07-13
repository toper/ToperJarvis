using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.Core.Tests.Speech;

public class SmartTurnOptionsTests
{
    [Fact]
    public void Domyslne_wartosci_sa_sensowne()
    {
        var o = new JarvisOptions();
        Assert.Equal("rms", o.Audio.EndpointEngine);
        Assert.EndsWith(".onnx", o.SmartTurn.ModelPath);
        Assert.InRange(o.SmartTurn.CompletionThreshold, 0f, 1f);
    }
}
