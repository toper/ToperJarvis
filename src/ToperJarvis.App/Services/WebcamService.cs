using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.App.Services;

/// <summary>
/// Mini-podgląd kamery. Pętla w tle pobiera klatki — z lokalnego urządzenia (OpenCvSharp) albo,
/// gdy ustawiono <see cref="CameraOptions.SnapshotUrl"/>, migawki kamery IP po HTTP — skaluje je do
/// miniatury i trzyma ostatnią jako JPEG (<see cref="LatestJpeg"/>). UI pobiera ją swoim tempem.
/// </summary>
public sealed class WebcamService : IDisposable
{
    private const int ThumbWidth = 320;

    private readonly ILogger<WebcamService> _logger;
    private readonly bool _enabled;
    private readonly int _index;
    private readonly int _fps;
    private readonly int _captureWidth;
    private readonly int _captureHeight;
    private readonly string _snapshotUrl;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile byte[]? _latest;

    public WebcamService(IOptions<JarvisOptions> options, ILogger<WebcamService> logger)
    {
        _logger = logger;
        var cam = options.Value.Camera;
        _enabled = cam.Enabled;
        _index = cam.DeviceIndex;
        _fps = cam.Fps > 0 ? cam.Fps : 8;
        _captureWidth = cam.CaptureWidth;
        _captureHeight = cam.CaptureHeight;
        _snapshotUrl = cam.SnapshotUrl ?? "";
    }

    /// <summary>Ostatnia klatka jako JPEG (lub null, gdy brak/niedostępna).</summary>
    public byte[]? LatestJpeg => _latest;

    public void Start()
    {
        if (!_enabled || _loop is not null)
            return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loop = string.IsNullOrWhiteSpace(_snapshotUrl)
            ? Task.Run(() => DeviceLoop(ct))
            : Task.Run(() => SnapshotLoopAsync(ct));
    }

    // Tryb lokalny: klatki z urządzenia OpenCvSharp (z opcjonalnym wymuszeniem natywnego trybu).
    private void DeviceLoop(CancellationToken ct)
    {
        VideoCapture? cap = null;
        try
        {
            cap = new VideoCapture(_index);
            if (!cap.IsOpened())
            {
                _logger.LogWarning("Kamera #{Index} niedostępna — podgląd wyłączony.", _index);
                return;
            }

            // Wymuś natywny tryb przechwytywania, gdy skonfigurowany — unika anamorficznego FullHD.
            if (_captureWidth > 0)
                cap.Set(VideoCaptureProperties.FrameWidth, _captureWidth);
            if (_captureHeight > 0)
                cap.Set(VideoCaptureProperties.FrameHeight, _captureHeight);

            var delay = Math.Max(1, 1000 / _fps);
            using var frame = new Mat();
            while (!ct.IsCancellationRequested)
            {
                if (cap.Read(frame) && !frame.Empty())
                {
                    var jpeg = ToThumbnailJpeg(frame);
                    if (jpeg is not null)
                        _latest = jpeg;
                }

                ct.WaitHandle.WaitOne(delay);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Błąd pętli kamery — podgląd zatrzymany.");
        }
        finally
        {
            cap?.Release();
            cap?.Dispose();
        }
    }

    // Tryb IP: cyklicznie pobiera migawkę po HTTP (Basic/Digest), dekoduje i skaluje do miniatury.
    private async Task SnapshotLoopAsync(CancellationToken ct)
    {
        HttpClient? http = null;
        try
        {
            var uri = new Uri(_snapshotUrl);
            var handler = new HttpClientHandler();
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':', 2);
                var user = Uri.UnescapeDataString(parts[0]);
                var pass = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                handler.Credentials = new NetworkCredential(user, pass);
            }

            // Adres bez danych logowania — uwierzytelnianie idzie przez NetworkCredential.
            var requestUrl = new UriBuilder(uri) { UserName = "", Password = "" }.Uri;
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var delay = Math.Max(1, 1000 / _fps);
            var loggedError = false;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var bytes = await http.GetByteArrayAsync(requestUrl, ct).ConfigureAwait(false);
                    using var frame = Cv2.ImDecode(bytes, ImreadModes.Color);
                    var jpeg = ToThumbnailJpeg(frame);
                    if (jpeg is not null)
                        _latest = jpeg;
                    loggedError = false;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Loguj raz na serię błędów — nie zasypuj logu przy chwilowej niedostępności kamery.
                    if (!loggedError)
                    {
                        _logger.LogWarning(ex, "Błąd pobierania migawki kamery — ponawiam.");
                        loggedError = true;
                    }
                }

                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie można skonfigurować migawki kamery — podgląd wyłączony.");
        }
        finally
        {
            http?.Dispose();
        }
    }

    // Skaluje klatkę do miniatury o szerokości ThumbWidth (zachowując proporcje) i koduje JPEG.
    private static byte[]? ToThumbnailJpeg(Mat frame)
    {
        if (frame.Empty())
            return null;

        using var thumb = new Mat();
        var height = frame.Width > 0 ? (int)Math.Round((double)ThumbWidth * frame.Height / frame.Width) : 180;
        Cv2.Resize(frame, thumb, new Size(ThumbWidth, Math.Max(1, height)));
        Cv2.ImEncode(".jpg", thumb, out var buf, new[] { (int)ImwriteFlags.JpegQuality, 70 });
        return buf;
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignoruj */ }
        // Poczekaj aż pętla się zakończy (zwolni kamerę) zanim zwolnimy CTS — inaczej dotknęłaby
        // już zwolnionego tokenu (ObjectDisposedException na wątku tła).
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignoruj */ }
        _cts?.Dispose();
        _latest = null;
    }
}
