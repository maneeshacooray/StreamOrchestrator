using System.Runtime.InteropServices;
using StreamOrchestrator.Display;
using StreamOrchestrator.Interop;

namespace StreamOrchestrator.Windows;

/// <summary>Repositions foreign top-level windows to fill a monitor, remembering their original
/// placement so they can be restored later.</summary>
public static class WindowMover
{
    // Original placement captured the first time each window is moved, keyed by handle.
    private static readonly Dictionary<IntPtr, NativeMethods.WINDOWPLACEMENT> Original = new();

    /// <summary>
    /// Restores (un-minimises/maximises) the window and sizes it to cover the monitor's bounds.
    /// Window styles are left intact so the move is non-destructive; the first move records the
    /// window's original placement for <see cref="RestoreAll"/>.
    /// </summary>
    public static bool MoveToMonitor(IntPtr hwnd, MonitorInfo monitor)
    {
        if (hwnd == IntPtr.Zero) return false;

        CapturePlacement(hwnd);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        return NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
            monitor.Left, monitor.Top, monitor.Width, monitor.Height,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>Restores every window moved this session to its original placement.</summary>
    public static void RestoreAll()
    {
        foreach (var (hwnd, placement) in Original)
        {
            var p = placement;
            NativeMethods.SetWindowPlacement(hwnd, ref p);
        }
        Original.Clear();
    }

    /// <summary>True if any moved windows can be restored.</summary>
    public static bool HasRestorable => Original.Count > 0;

    private static void CapturePlacement(IntPtr hwnd)
    {
        if (Original.ContainsKey(hwnd)) return;
        var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (NativeMethods.GetWindowPlacement(hwnd, ref wp))
            Original[hwnd] = wp;
    }
}
