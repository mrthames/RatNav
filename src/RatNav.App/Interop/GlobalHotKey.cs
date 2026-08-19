using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace RatNav.App.Interop;

/// <summary>
/// Registers system-wide hotkeys with Windows.
///
/// <para><b>Deliberately <c>RegisterHotKey</c> rather than a low-level keyboard hook.</b> A hook
/// sees every keystroke on the machine, which is what keyloggers do and what antivirus and
/// anti-cheat software is built to notice. <c>RegisterHotKey</c> asks the OS to deliver one
/// specific combination and nothing else — it cannot observe anything the player did not press on
/// purpose, and it does not touch the game.</para>
///
/// <para>Registration fails, quietly, if another application already owns a combination. That is
/// worth surfacing in settings rather than silently doing nothing when someone presses a key they
/// think they bound.</para>
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = [];
    private int _nextId = 1;

    public GlobalHotKey(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle) ?? throw new InvalidOperationException(
            "The window has no handle yet — register hotkeys after it is shown.");

        _source.AddHook(OnMessage);
    }

    /// <summary>
    /// Binds a combination. Returns false when something else already owns it, so the caller can
    /// tell the player rather than leaving them pressing a key that does nothing.
    /// </summary>
    public bool Register(ModifierKeys modifiers, Key key, Action action)
    {
        var id = _nextId++;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (!RegisterHotKey(_handle, id, (uint)modifiers, virtualKey)) return false;

        _actions[id] = action;
        return true;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_HOTKEY) return IntPtr.Zero;

        if (_actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Drops every binding, keeping the hook. Used when hotkeys are changed in Setup: Windows owns
    /// the old combination until it is released, so rebinding without this leaks it and the new
    /// key silently fails to register.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_handle, id);
        _actions.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(OnMessage);
    }
}
