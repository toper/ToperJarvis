using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions.Configuration;
using ToperJarvis.Abstractions.Speech;

namespace ToperJarvis.Speech.Audio;

/// <summary>
/// Wybór urządzenia wyjściowego MME. Przechowuje wybór i mapuje nazwę na numer urządzenia
/// <see cref="NAudio.Wave.WaveOutEvent.DeviceNumber"/> (<c>-1</c> = systemowe domyślne). Enumeracja
/// przez <c>winmm.dll</c> (klasa NAudio <c>WaveOut</c> nie jest dostępna w buildzie netstandard).
/// </summary>
public sealed class NAudioOutput : IAudioOutput
{
    private readonly ILogger<NAudioOutput> _logger;
    private string? _deviceName;
    private int _deviceNumber;

    public NAudioOutput(IOptions<JarvisOptions> options, ILogger<NAudioOutput> logger)
    {
        _logger = logger;
        var configured = options.Value.Audio.OutputDeviceName;
        _deviceName = string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
        _deviceNumber = ResolveDeviceNumber(_deviceName);
    }

    public string? SelectedDeviceName => _deviceName;

    // Buforowany numer — enumeracja winmm tylko przy zmianie urządzenia, nie przy każdym odtworzeniu.
    public int DeviceNumber => _deviceNumber;

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices()
    {
        var count = waveOutGetNumDevs();
        var devices = new List<AudioOutputDevice>(count);
        for (var i = 0; i < count; i++)
        {
            var caps = new WaveOutCaps();
            if (waveOutGetDevCaps((nuint)i, ref caps, Marshal.SizeOf<WaveOutCaps>()) == 0)
                devices.Add(new AudioOutputDevice(i, caps.szPname));
        }

        return devices;
    }

    public void SelectDevice(string? deviceName)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim();
        if (normalized == _deviceName)
            return;

        _deviceName = normalized;
        _deviceNumber = ResolveDeviceNumber(normalized);
        _logger.LogInformation("Zmiana wyjścia audio na: {Device}", normalized ?? "(domyślne)");
    }

    /// <summary>Mapuje nazwę na numer urządzenia. Puste/brak dopasowania = -1 (systemowe domyślne).</summary>
    private int ResolveDeviceNumber(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return -1;

        foreach (var device in GetOutputDevices())
        {
            if (string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase))
                return device.Index;
        }

        _logger.LogWarning("Nie znaleziono wyjścia '{Device}' — używam domyślnego.", deviceName);
        return -1;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetDevCapsW")]
    private static extern int waveOutGetDevCaps(nuint deviceId, ref WaveOutCaps caps, int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaveOutCaps
    {
        public short wMid;
        public short wPid;
        public int vDriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;

        public int dwFormats;
        public short wChannels;
        public short wReserved1;
        public int dwSupport;
    }
}
