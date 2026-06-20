using System.Runtime.InteropServices;

namespace ToperJarvis.Tools.Input;

/// <summary>Symulacja wejścia (klawiatura/mysz) przez Win32 SendInput / mouse_event.</summary>
internal static class InputSimulator
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008, MouseRightUp = 0x0010;
    private const uint MouseWheel = 0x0800;

    /// <summary>Wpisuje tekst znak po znaku (Unicode).</summary>
    public static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            SendKeyboard(0, ch, KeyEventUnicode);
            SendKeyboard(0, ch, KeyEventUnicode | KeyEventKeyUp);
        }
    }

    /// <summary>Wciska i zwalnia pojedynczy klawisz (VK).</summary>
    public static void PressKey(byte vk)
    {
        SendKeyboard(vk, 0, 0);
        SendKeyboard(vk, 0, KeyEventKeyUp);
    }

    /// <summary>Wykonuje skrót: wciska po kolei wszystkie klawisze, zwalnia w odwrotnej kolejności.</summary>
    public static void Hotkey(IReadOnlyList<byte> keys)
    {
        foreach (var vk in keys)
            SendKeyboard(vk, 0, 0);
        for (var i = keys.Count - 1; i >= 0; i--)
            SendKeyboard(keys[i], 0, KeyEventKeyUp);
    }

    public static void MoveMouse(int x, int y) => SetCursorPos(x, y);

    public static void Click(bool right = false)
    {
        if (right)
        {
            mouse_event(MouseRightDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseRightUp, 0, 0, 0, UIntPtr.Zero);
        }
        else
        {
            mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        }
    }

    public static void Scroll(int amount) => mouse_event(MouseWheel, 0, 0, amount, UIntPtr.Zero);

    private static void SendKeyboard(ushort vk, ushort scan, uint flags)
    {
        var input = new INPUT
        {
            type = InputKeyboard,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);
}
