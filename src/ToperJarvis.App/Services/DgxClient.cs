using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.App.Services;

/// <summary>
/// Monitoring DGX przez jeden trwały strumień SSH: <c>nvidia-smi ... -l N</c> wypisuje linię co N s,
/// którą czytamy i parsujemy. Minimalne obciążenie (jedno połączenie, jedna komenda). Klucz SSH z
/// domyślnego ~/.ssh. Przy zerwaniu połączenia automatyczny restart.
/// </summary>
public sealed class DgxClient : IDisposable
{
    private readonly ILogger<DgxClient> _logger;
    private readonly bool _enabled;
    private readonly string _host;
    private readonly string _user;
    private readonly int _poll;

    private CancellationTokenSource? _cts;
    private Process? _proc;

    public DgxClient(IOptions<JarvisOptions> options, ILogger<DgxClient> logger)
    {
        _logger = logger;
        var dgx = options.Value.Dgx;
        _host = dgx.Host;
        _user = dgx.User;
        _poll = dgx.PollSeconds > 0 ? dgx.PollSeconds : 5;
        _enabled = dgx.Enabled && !string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_user);
    }

    /// <summary>Ostatnie metryki GPU lub null, gdy brak danych.</summary>
    public double? GpuUtil { get; private set; }
    public double? PowerW { get; private set; }
    public double? TempC { get; private set; }

    public void Start()
    {
        if (!_enabled || _cts is not null)
            return;

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var query = "nvidia-smi --query-gpu=utilization.gpu,power.draw,temperature.gpu " +
                    $"--format=csv,noheader,nounits -l {_poll}";

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ssh",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add("BatchMode=yes");
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add("ServerAliveInterval=15");
                psi.ArgumentList.Add($"{_user}@{_host}");
                psi.ArgumentList.Add(query);

                _proc = new Process { StartInfo = psi };
                _proc.Start();
                _ = _proc.StandardError.ReadToEndAsync(ct); // drenaż

                while (!ct.IsCancellationRequested)
                {
                    var line = await _proc.StandardOutput.ReadLineAsync(ct);
                    if (line is null)
                        break; // proces zakończony — restart

                    Parse(line);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DGX: błąd strumienia SSH — ponawiam.");
            }

            GpuUtil = PowerW = TempC = null; // brak świeżych danych
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
        }
    }

    private void Parse(string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 3)
            return;

        GpuUtil = TryNum(parts[0]);
        PowerW = TryNum(parts[1]);
        TempC = TryNum(parts[2]);
    }

    private static double? TryNum(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignoruj */ }
        try { _proc?.Kill(true); } catch { /* ignoruj */ }
        _proc?.Dispose();
        _cts?.Dispose();
    }
}
