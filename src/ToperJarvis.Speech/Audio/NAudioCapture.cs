using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.Audio;

/// <summary>
/// Przechwytywanie audio z mikrofonu przez NAudio (<see cref="WaveInEvent"/>).
/// Strumień: mono, 16-bit PCM → konwertowany do float32 (-1..1) i emitowany ramkami co ~100 ms.
/// Urządzenie wybierane po nazwie (z konfiguracji lub menu w trayu); puste = pierwsze/domyślne.
/// </summary>
public sealed class NAudioCapture : IAudioCapture
{
    private readonly int _sampleRate;
    private readonly ILogger<NAudioCapture> _logger;
    private readonly object _gate = new();
    private string? _deviceName;
    private WaveInEvent? _waveIn;

    // Diagnostyka „czy mikrofon w ogóle słyszy" — log poziomu sygnału co ~3 s.
    private int _framesSinceLog;
    private float _peakSinceLog;

    public NAudioCapture(IOptions<JarvisOptions> options, ILogger<NAudioCapture> logger)
    {
        _sampleRate = options.Value.Audio.SampleRate;
        _logger = logger;
        var configured = options.Value.Audio.InputDeviceName;
        _deviceName = string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }

    public int SampleRate => _sampleRate;

    public string? SelectedDeviceName => _deviceName;

    public event EventHandler<AudioFrame>? FrameAvailable;

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var devices = new List<AudioInputDevice>(WaveInEvent.DeviceCount);
        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            devices.Add(new AudioInputDevice(i, WaveInEvent.GetCapabilities(i).ProductName));
        return devices;
    }

    public void Start()
    {
        lock (_gate)
            StartInternal();
    }

    public void Stop()
    {
        lock (_gate)
            StopInternal();
    }

    public void SelectDevice(string? deviceName)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();
        lock (_gate)
        {
            if (normalized == _deviceName)
                return;

            _deviceName = normalized;
            _logger.LogInformation("Zmiana mikrofonu na: {Device}", normalized ?? "(domyślny)");

            // Przełączenie w locie: subskrybenci są podpięci do FrameAvailable tej klasy,
            // więc wystarczy odtworzyć wewnętrzny WaveInEvent z nowym urządzeniem.
            if (_waveIn is not null)
            {
                StopInternal();
                StartInternal();
            }
        }
    }

    private void StartInternal()
    {
        if (_waveIn is not null)
            return;

        var deviceNumber = ResolveDeviceNumber(_deviceName);

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(_sampleRate, 16, 1),
            BufferMilliseconds = 100,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();

        var name = deviceNumber >= 0 && deviceNumber < WaveInEvent.DeviceCount
            ? WaveInEvent.GetCapabilities(deviceNumber).ProductName
            : "(domyślny)";
        _logger.LogInformation(
            "Mikrofon uruchomiony: #{Index} '{Name}' @ {SampleRate} Hz.", deviceNumber, name, _sampleRate);
    }

    private void StopInternal()
    {
        if (_waveIn is null)
            return;

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
    }

    /// <summary>Mapuje nazwę urządzenia na indeks WaveIn. Brak dopasowania/puste = 0 (pierwsze).</summary>
    private int ResolveDeviceNumber(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return 0;

        for (var i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            if (string.Equals(WaveInEvent.GetCapabilities(i).ProductName, deviceName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        _logger.LogWarning(
            "Nie znaleziono mikrofonu '{Device}' — używam pierwszego dostępnego.", deviceName);
        return 0;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var handler = FrameAvailable;
        if (handler is null)
            return;

        var sampleCount = e.BytesRecorded / 2; // 16-bit = 2 bajty/próbkę
        var samples = new float[sampleCount];
        var peak = 0f;
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
            var value = sample / 32768f;
            samples[i] = value;
            var abs = value < 0 ? -value : value;
            if (abs > peak)
                peak = abs;
        }

        LogSignalLevel(peak);
        handler(this, new AudioFrame(samples, _sampleRate));
    }

    // Co ~3 s loguje szczytowy poziom sygnału — pozwala potwierdzić, że mikrofon faktycznie słyszy.
    private void LogSignalLevel(float framePeak)
    {
        if (framePeak > _peakSinceLog)
            _peakSinceLog = framePeak;

        if (++_framesSinceLog < 30)
            return;

        _logger.LogInformation("Poziom mikrofonu (szczyt z ~3 s): {Peak:P0}.", _peakSinceLog);
        _framesSinceLog = 0;
        _peakSinceLog = 0f;
    }

    public void Dispose() => Stop();
}
