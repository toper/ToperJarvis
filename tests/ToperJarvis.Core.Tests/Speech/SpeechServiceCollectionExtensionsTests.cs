using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.Speech;

namespace ToperJarvis.Core.Tests.Speech;

public class SpeechServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData("nieznany")]
    [InlineData("openwakewrd")]
    [InlineData("Porcupin")]
    public void Nieznany_engine_rzuca_zamiast_cichego_fallbacku(string engine)
    {
        var options = new JarvisOptions();
        options.WakeWord.Engine = engine;

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<JarvisOptions>>(Options.Create(options));
        services.AddJarvisSpeech();

        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => { provider.GetRequiredService<IWakeWordDetector>(); });
    }
}
