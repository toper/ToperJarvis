using System.Diagnostics;
using System.Globalization;

namespace ToperJarvis.Tools.Vision;

/// <summary>
/// Cienka otoczka na proces ffmpeg — dekoduje ścieżkę audio z pliku audio lub wideo do surowego
/// PCM (mono float32) o zadanej częstotliwości, gotowego dla <c>ISpeechToText</c>.
/// </summary>
internal static class Ffmpeg
{
    /// <summary>
    /// Dekoduje audio z <paramref name="filePath"/> (audio lub wideo) do próbek mono float32.
    /// Rzuca <see cref="FfmpegException"/> przy braku ffmpeg lub błędzie dekodowania.
    /// </summary>
    public static async Task<float[]> DecodeToPcmAsync(
        string ffmpegPath, string filePath, int sampleRate, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
                 {
                     "-i", filePath,
                     "-vn",                 // pomiń wideo
                     "-ac", "1",            // mono
                     "-ar", sampleRate.ToString(CultureInfo.InvariantCulture),
                     "-f", "f32le",         // surowy float32 little-endian
                     "-",                   // wyjście na stdout
                 })
            psi.ArgumentList.Add(arg);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new FfmpegException("Nie udało się uruchomić ffmpeg.");
        }
        catch (Exception ex) when (ex is not FfmpegException)
        {
            throw new FfmpegException($"Nie znaleziono ffmpeg ({ffmpegPath}). Zainstaluj ffmpeg lub ustaw Media:FfmpegPath.");
        }

        using (process)
        {
            using var output = new MemoryStream();
            var copyTask = process.StandardOutput.BaseStream.CopyToAsync(output, ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await copyTask;

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                var tail = error.Length > 300 ? error[^300..] : error;
                throw new FfmpegException($"ffmpeg zakończył się błędem (kod {process.ExitCode}): {tail}");
            }

            return PcmBytesToFloats(output.ToArray());
        }
    }

    /// <summary>Konwertuje surowe bajty float32 little-endian na tablicę próbek (ucina niepełny ogon).</summary>
    internal static float[] PcmBytesToFloats(byte[] bytes)
    {
        var count = bytes.Length / sizeof(float);
        var samples = new float[count];
        Buffer.BlockCopy(bytes, 0, samples, 0, count * sizeof(float));
        return samples;
    }
}

/// <summary>Błąd uruchomienia/dekodowania ffmpeg — wołający zamienia go na przyjazny komunikat.</summary>
internal sealed class FfmpegException(string message) : Exception(message);
