using System.Windows.Interop;
using StreamOrchestrator.Interop;

namespace StreamOrchestrator.Input;

/// <summary>
/// Registers system-wide hotkeys against a window handle and dispatches them to callbacks, so
/// move/swap/on-top work even while the fullscreen stream has focus.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd)
                  ?? throw new InvalidOperationException("No HwndSource for the window handle.");
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey. Returns false if the OS rejected it (e.g. already in use).</summary>
    public bool Register(uint modifiers, uint virtualKey, Action callback)
    {
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_hwnd, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
            return false;
        _actions[id] = callback;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            NativeMethods.UnregisterHotKey(_hwnd, id);
        _actions.Clear();
        _source.RemoveHook(WndProc);
    }
}
