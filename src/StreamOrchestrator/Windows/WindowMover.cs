using StreamOrchestrator.Display;
using StreamOrchestrator.Interop;

namespace StreamOrchestrator.Windows;

/// <summary>Repositions a foreign top-level window to fill a given monitor.</summary>
public static class WindowMover
{
    /// <summary>
    /// Restores (un-minimises/maximises) the window and sizes it to cover the monitor's bounds.
    /// Window styles are left intact so the move is non-destructive and easily undone by the user;
    /// windows that are already borderless (e.g. a PowerPoint slideshow) fill the screen exactly.
    /// </summary>
    public static bool MoveToMonitor(IntPtr hwnd, MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero) return false;

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        return NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
            monitor.Left, monitor.Top, monitor.Width, monitor.Height,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);
    }
}
