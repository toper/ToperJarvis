using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.Speech;
using ToperJarvis.Speech.Tts;

namespace ToperJarvis.Core.Tests.Speech;

public class TtsRegistrationTests
{
    private static ServiceProvider BuildProvider(bool cacheEnabled)
    {
        var options = new JarvisOptions();
        options.Tts.CacheEnabled = cacheEnabled;

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<JarvisOptions>>(Options.Create(options));
        services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddJarvisSpeech();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Cache_wlaczony_rejestruje_dekorator()
    {
        using var provider = BuildProvider(cacheEnabled: true);

        var tts = provider.GetRequiredService<ITextToSpeech>();

        Assert.IsType<CachingTextToSpeech>(tts);
    }

    [Fact]
    public void Cache_wylaczony_rejestruje_konkret_piper()
    {
        using var provider = BuildProvider(cacheEnabled: false);

        var tts = provider.GetRequiredService<ITextToSpeech>();

        Assert.IsType<PiperTextToSpeech>(tts);
    }
}
