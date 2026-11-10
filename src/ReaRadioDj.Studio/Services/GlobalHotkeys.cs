using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// Hotkeys that also work when another program has the focus (Windows RegisterHotKey). A key that another program
/// already claimed cannot be registered; <see cref="Apply"/> reports those.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const uint WmHotkey = 0x0312;
    private const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModWin = 8, ModNoRepeat = 0x4000;

    private readonly IntPtr _hwnd;
    private readonly Action<string> _run;
    private readonly Dictionary<int, string> _commands = [];
    private int _nextId = 0xB000;

    private GlobalHotkeys(IntPtr hwnd, Action<string> run) { _hwnd = hwnd; _run = run; }

    /// <summary>Null when not on Windows or the window has no handle yet.</summary>
    public static GlobalHotkeys? For(Window window, Action<string> run)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return null;
        var g = new GlobalHotkeys(hwnd, run);
        Win32Properties.AddWndProcHookCallback(window, g.Hook);
        return g;
    }

    private IntPtr Hook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _commands.TryGetValue((int)wParam, out var command))
        {
            handled = true;
            _run(command);
        }
        return IntPtr.Zero;
    }

    /// <summary>Registers these keys (and frees the earlier ones). Returns the keys that could not be registered.</summary>
    public IReadOnlyList<string> Apply(IEnumerable<(KeyGesture Gesture, string Command)> keys)
    {
        Clear();
        var failed = new List<string>();
        foreach (var (gesture, command) in keys)
        {
            if (VirtualKey(gesture.Key) is not uint vk) { failed.Add(gesture.ToString()); continue; }
            uint mods = ModNoRepeat;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) mods |= ModAlt;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) mods |= ModControl;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) mods |= ModShift;
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) mods |= ModWin;
            int id = _nextId++;
            if (RegisterHotKey(_hwnd, id, mods, vk)) _commands[id] = command;
            else failed.Add(gesture.ToString());
        }
        return failed;
    }

    private void Clear()
    {
        foreach (var id in _commands.Keys) UnregisterHotKey(_hwnd, id);
        _commands.Clear();
    }

    public void Dispose() => Clear();

    /// <summary>The Windows virtual key of an Avalonia key: letters, digits, F1-F24, numeric keypad and the usual named keys.</summary>
    public static uint? VirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (uint)(0x41 + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)(0x30 + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (uint)(0x60 + (key - Key.NumPad0)),
        >= Key.F1 and <= Key.F24 => (uint)(0x70 + (key - Key.F1)),
        Key.Space => 0x20, Key.Enter => 0x0D, Key.Tab => 0x09, Key.Escape => 0x1B, Key.Back => 0x08,
        Key.PageUp => 0x21, Key.PageDown => 0x22, Key.End => 0x23, Key.Home => 0x24,
        Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28, Key.Insert => 0x2D, Key.Delete => 0x2E,
        Key.Multiply => 0x6A, Key.Add => 0x6B, Key.Subtract => 0x6D, Key.Decimal => 0x6E, Key.Divide => 0x6F,
        Key.Pause => 0x13, Key.Scroll => 0x91,
        Key.MediaPlayPause => 0xB3, Key.MediaStop => 0xB2, Key.MediaNextTrack => 0xB0, Key.MediaPreviousTrack => 0xB1,
        _ => null,
    };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
