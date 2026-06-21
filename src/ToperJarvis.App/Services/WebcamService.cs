using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCvSharp;
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.App.Services;

/// <summary>
/// Mini-podgląd z lokalnej kamery (OpenCvSharp). Pętla w tle pobiera klatki, skaluje do miniatury
/// i trzyma ostatnią jako JPEG (<see cref="LatestJpeg"/>) — UI pobiera ją swoim tempem.
/// </summary>
public sealed class WebcamService : IDisposable
{
    private readonly ILogger<WebcamService> _logger;
    private readonly bool _enabled;
    private readonly int _index;
    private readonly int _fps;

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
    }

    /// <summary>Ostatnia klatka jako JPEG (lub null, gdy brak/niedostępna).</summary>
    public byte[]? LatestJpeg => _latest;

    public void Start()
    {
        if (!_enabled || _loop is not null)
            return;

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => CaptureLoop(_cts.Token));
    }

    private void CaptureLoop(CancellationToken ct)
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

            var delay = Math.Max(1, 1000 / _fps);
            using var frame = new Mat();
            while (!ct.IsCancellationRequested)
            {
                if (cap.Read(frame) && !frame.Empty())
                {
                    using var thumb = new Mat();
                    // Zachowaj proporcje kamery (np. 16:9) — skaluj do szerokości 320.
                    var height = frame.Width > 0 ? (int)Math.Round(320.0 * frame.Height / frame.Width) : 180;
                    Cv2.Resize(frame, thumb, new Size(320, Math.Max(1, height)));
                    Cv2.ImEncode(".jpg", thumb, out var buf, new[] { (int)ImwriteFlags.JpegQuality, 70 });
                    _latest = buf;
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

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignoruj */ }
        _cts?.Dispose();
        _latest = null;
    }
}
