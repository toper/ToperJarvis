using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ToperJarvis.App.Services;

/// <summary>
/// Dostarcza bieżące metryki systemu (CPU%, RAM%) odświeżane cyklicznie. Używane przez HUD.
/// </summary>
public sealed partial class SystemMetricsService : ObservableObject, IDisposable
{
    private readonly PerformanceCounter? _cpu;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _ramPercent;

    public SystemMetricsService()
    {
        try
        {
            _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpu.NextValue(); // pierwszy odczyt zawsze 0 — „rozgrzewamy"
        }
        catch
        {
            _cpu = null; // brak licznika (np. uprawnienia) — CPU pozostanie 0
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
    }

    private void Update()
    {
        if (_cpu is not null)
        {
            try { CpuPercent = Math.Round(_cpu.NextValue(), 0); }
            catch { /* ignoruj chwilowe błędy licznika */ }
        }

        RamPercent = ReadRamPercent();
    }

    private static double ReadRamPercent()
    {
        var status = new MemoryStatusEx();
        return GlobalMemoryStatusEx(status) ? status.dwMemoryLoad : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    public void Dispose()
    {
        _timer.Stop();
        _cpu?.Dispose();
    }
}
