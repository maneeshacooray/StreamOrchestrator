using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StreamOrchestrator.Player;

/// <summary>
/// Hosts a native child window inside the WPF visual tree. libmpv renders video directly into this
/// window's HWND (passed as its <c>wid</c>), which keeps decoding/compositing off the WPF render
/// thread — essential for low-latency playback.
/// </summary>
public sealed class MpvHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    private IntPtr _hwndChild;

    /// <summary>Raised once the native child window exists; the argument is its HWND.</summary>
    public event Action<IntPtr>? HostReady;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwndChild = CreateWindowEx(
            0, "static", string.Empty,
            WS_CHILD | WS_VISIBLE,
            0, 0, 0, 0,
            hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwndChild == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create the mpv host window.");

        HostReady?.Invoke(_hwndChild);
        return new HandleRef(this, _hwndChild);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_hwndChild != IntPtr.Zero)
        {
            DestroyWindow(_hwndChild);
            _hwndChild = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);
}
