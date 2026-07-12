using ToperJarvis.Speech.Tts;

namespace ToperJarvis.Core.Tests.Speech;

public class RawPcmPlayerTests
{
    [Fact]
    public async Task Pusty_strumien_konczy_sie_bez_bledu()
    {
        var player = new RawPcmPlayer(deviceNumber: -1, sampleRate: 22050);
        using var ms = new MemoryStream(Array.Empty<byte>());

        await player.PlayAsync(ms, CancellationToken.None);
    }

    [Fact]
    public async Task Anulowanie_przerywa_odtwarzanie()
    {
        var player = new RawPcmPlayer(-1, 22050);
        using var ms = new MemoryStream(new byte[22050 * 2 * 5]); // 5 s ciszy
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        var play = player.PlayAsync(ms, cts.Token);
        var winner = await Task.WhenAny(play, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(play, winner);
        // Odtwarzanie mogło zakończyć się normalnie albo anulowaniem — liczy się tylko brak zawiśnięcia.
        try { await play; }
        catch (OperationCanceledException) { /* oczekiwane */ }
    }
}
