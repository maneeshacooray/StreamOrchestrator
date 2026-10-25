using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace StreamOrchestrator.Player;

/// <summary>
/// Hosts a native child window inside the WPF visual tree. libmpv renders video directly into this
/// window's HWND (passed as its <c>wid</c>), which keeps decoding/compositing off the WPF render
/// thread — essential for low-latency playback.
///
/// The window is created from a private class whose background brush is black, so while mpv is not
/// painting (loading, between files, or after stop) the surface erases to black instead of the
/// default white.
/// </summary>
public sealed class MpvHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const uint CS_HREDRAW = 0x0002;
    private const uint CS_VREDRAW = 0x0001;
    private const int BLACK_BRUSH = 4;
    private const string ClassName = "StreamOrchestratorMpvHost";

    // Keep the delegate alive for the lifetime of the process so the WndProc pointer stays valid.
    private static readonly WndProcDelegate s_wndProc = DefWindowProcW;
    private static bool s_registered;
    private static readonly object s_lock = new();

    private IntPtr _hwndChild;

    /// <summary>Raised once the native child window exists; the argument is its HWND.</summary>
    public event Action<IntPtr>? HostReady;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        EnsureClassRegistered();

        _hwndChild = CreateWindowEx(
            0, ClassName, string.Empty,
            WS_CHILD | WS_VISIBLE,
            0, 0, 0, 0,
            hwndParent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

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

    private static void EnsureClassRegistered()
    {
        lock (s_lock)
        {
            if (s_registered) return;

            var wc = new WNDCLASS
            {
                style = CS_HREDRAW | CS_VREDRAW,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_wndProc),
                hInstance = GetModuleHandle(null),
                hbrBackground = GetStockObject(BLACK_BRUSH),
                lpszClassName = ClassName,
            };

            if (RegisterClass(ref wc) == 0)
            {
                // 1410 = ERROR_CLASS_ALREADY_EXISTS (e.g. re-registered); treat as success.
                int err = Marshal.GetLastWin32Error();
                if (err != 1410)
                    throw new InvalidOperationException($"Failed to register mpv host window class (Win32 {err}).");
            }
            s_registered = true;
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
