using System.Diagnostics;
using System.Globalization;

namespace ToperJarvis.Tools.Vision;

/// <summary>
/// Cienka otoczka na proces ffmpeg — dekoduje ścieżkę audio z pliku audio lub wideo do surowego
/// PCM (mono float32) o zadanej częstotliwości, gotowego dla <c>ISpeechToText</c>.
/// </summary>
internal static class Ffmpeg
{
    // Twardy bezpiecznik na rozmiar zdekodowanego PCM (~140 min mono f32 @ 16 kHz) — chroni
    // przed wyczerpaniem pamięci, gdy mały plik wideo zawiera bardzo długą ścieżkę audio.
    private const long MaxPcmBytes = 512L * 1024 * 1024;

    /// <summary>
    /// Dekoduje audio z <paramref name="filePath"/> (audio lub wideo) do próbek mono float32.
    /// Rzuca <see cref="FfmpegException"/> przy braku ffmpeg, błędzie dekodowania lub zbyt długim nagraniu.
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
            // stderr czytamy współbieżnie ze stdout, by nie zapełnić bufora pipe (klasyczny deadlock).
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            try
            {
                using var output = new MemoryStream();
                var buffer = new byte[81920];
                var stdout = process.StandardOutput.BaseStream;
                int read;
                while ((read = await stdout.ReadAsync(buffer, ct)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > MaxPcmBytes)
                        throw new FfmpegException("Nagranie jest zbyt długie do transkrypcji.");
                }

                await process.WaitForExitAsync(ct);
                var error = await errorTask;

                if (process.ExitCode != 0)
                {
                    var tail = error.Length > 300 ? error[^300..] : error;
                    throw new FfmpegException($"ffmpeg zakończył się błędem (kod {process.ExitCode}): {tail}");
                }

                return PcmBytesToFloats(output.ToArray());
            }
            catch (Exception ex) when (ex is OperationCanceledException or FfmpegException)
            {
                // Anulowanie lub przekroczenie limitu — ubij ffmpeg, by nie został sierotą.
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* już zakończony */ }
                try { await errorTask; } catch { /* obserwacja, ignorujemy */ }
                throw;
            }
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
