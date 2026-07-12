using Microsoft.Extensions.Logging.Abstractions;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;
using ToperJarvis.Speech.Tts;

namespace ToperJarvis.Core.Tests.Speech;

public class CachingTextToSpeechTests
{
    private sealed class CountingSynth : IPcmSynthesizer
    {
        public int Calls;
        public int SampleRate => 22050;

        public Task<byte[]> SynthesizeToPcmAsync(string text, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new byte[320]);
        }
    }

    private sealed class FakeAudioOutput : IAudioOutput
    {
        public string? SelectedDeviceName => null;
        public int DeviceNumber => -1;
        public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => Array.Empty<AudioOutputDevice>();
        public void SelectDevice(string? deviceName) { }
    }

    [Fact]
    public async Task Fraza_filler_syntezowana_tylko_raz()
    {
        var synth = new CountingSynth();
        var opts = new TtsOptions { CacheEnabled = true, FillerPhrases = new() { "Chwileczkę." } };
        var tts = new CachingTextToSpeech(synth, new FakeAudioOutput(), opts, NullLogger<CachingTextToSpeech>.Instance);

        await tts.SpeakAsync("Chwileczkę.");
        await tts.SpeakAsync("Chwileczkę.");

        Assert.Equal(1, synth.Calls);
    }

    [Fact]
    public async Task Fraza_spoza_cache_syntezowana_za_kazdym_razem()
    {
        var synth = new CountingSynth();
        var opts = new TtsOptions { CacheEnabled = true, FillerPhrases = new() { "Chwileczkę." } };
        var tts = new CachingTextToSpeech(synth, new FakeAudioOutput(), opts, NullLogger<CachingTextToSpeech>.Instance);

        await tts.SpeakAsync("Losowe zdanie z LLM.");
        await tts.SpeakAsync("Losowe zdanie z LLM.");

        Assert.Equal(2, synth.Calls);
    }

    [Fact]
    public async Task WarmupAsync_wypelnia_cache()
    {
        var synth = new CountingSynth();
        var opts = new TtsOptions { CacheEnabled = true, FillerPhrases = new() { "Chwileczkę." } };
        var tts = new CachingTextToSpeech(synth, new FakeAudioOutput(), opts, NullLogger<CachingTextToSpeech>.Instance);

        await tts.WarmupAsync();
        var callsAfterWarmup = synth.Calls;
        await tts.SpeakAsync("Chwileczkę.");

        Assert.Equal(callsAfterWarmup, synth.Calls);
    }

    [Fact]
    public async Task Cache_wylaczony_zawsze_syntezuje()
    {
        var synth = new CountingSynth();
        var opts = new TtsOptions { CacheEnabled = false, FillerPhrases = new() { "Chwileczkę." } };
        var tts = new CachingTextToSpeech(synth, new FakeAudioOutput(), opts, NullLogger<CachingTextToSpeech>.Instance);

        await tts.SpeakAsync("Chwileczkę.");
        await tts.SpeakAsync("Chwileczkę.");

        Assert.Equal(2, synth.Calls);
    }
}
