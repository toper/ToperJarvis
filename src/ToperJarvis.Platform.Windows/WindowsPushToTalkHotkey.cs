using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ToperJarvis.Abstractions;
using ToperJarvis.Abstractions.Configuration;

namespace ToperJarvis.Platform.Windows;

/// <summary>
/// Globalny push-to-talk przez low-level keyboard hook (WH_KEYBOARD_LL). Reaguje na konfigurowany
/// klawisz niezależnie od fokusu okna; zgłasza <see cref="Pressed"/> przy wciśnięciu i
/// <see cref="Released"/> przy puszczeniu. Hook instaluje się na wątku UI (pompuje komunikaty).
/// </summary>
public sealed class WindowsPushToTalkHotkey : IPushToTalkHotkey
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly ILogger<WindowsPushToTalkHotkey> _logger;
    private readonly bool _enabled;
    private readonly int _vk;
    private readonly string _keyName;

    private LowLevelKeyboardProc? _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _down;

    public WindowsPushToTalkHotkey(IOptions<JarvisOptions> options, ILogger<WindowsPushToTalkHotkey> logger)
    {
        _logger = logger;
        var ptt = options.Value.PushToTalk;
        _enabled = ptt.Enabled;
        _keyName = ptt.Key;
        _vk = MapKey(ptt.Key);
    }

    public event EventHandler? Pressed;
    public event EventHandler? Released;

    public void Start()
    {
        if (!_enabled || _hook != IntPtr.Zero)
            return;

        _proc = HookCallback; // pole — chroni delegat przed GC
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);

        if (_hook == IntPtr.Zero)
            _logger.LogWarning("Nie udało się zainstalować hooka push-to-talk (klawisz {Key}).", _keyName);
        else
            _logger.LogInformation("Push-to-talk aktywny: przytrzymaj klawisz {Key}.", _keyName);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            if (vk == _vk)
            {
                var msg = (int)wParam;
                if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    if (!_down)
                    {
                        _down = true;
                        Pressed?.Invoke(this, EventArgs.Empty);
                    }
                }
                else if (msg is WM_KEYUP or WM_SYSKEYUP)
                {
                    if (_down)
                    {
                        _down = false;
                        Released?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Mapuje nazwę klawisza na kod wirtualny (VK). Nieznana = RightCtrl.</summary>
    private static int MapKey(string key) => key.Trim().ToLowerInvariant() switch
    {
        "rightctrl" or "rctrl" => 0xA3,
        "leftctrl" or "lctrl" => 0xA2,
        "rightshift" or "rshift" => 0xA1,
        "leftshift" or "lshift" => 0xA0,
        "rightalt" or "ralt" => 0xA5,
        "pause" => 0x13,
        "scrolllock" => 0x91,
        "capslock" => 0x14,
        "f8" => 0x77,
        "f9" => 0x78,
        "f10" => 0x79,
        "insert" => 0x2D,
        "end" => 0x23,
        _ => 0xA3,
    };

    public void Dispose() => Stop();

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
