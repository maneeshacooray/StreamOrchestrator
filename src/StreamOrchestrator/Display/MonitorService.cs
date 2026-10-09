using System.Runtime.InteropServices;
using StreamOrchestrator.Interop;

namespace StreamOrchestrator.Display;

/// <summary>A connected display and its physical-pixel bounds.</summary>
public sealed record MonitorInfo(int Index, int Left, int Top, int Width, int Height, bool IsPrimary, IntPtr Handle)
{
    public string Label =>
        $"Display {Index + 1}{(IsPrimary ? " (primary)" : "")} — {Width}×{Height}";
}

/// <summary>Enumerates connected monitors via Win32.</summary>
public static class MonitorService
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var result = new List<(IntPtr h, NativeMethods.RECT r)>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMon, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (NativeMethods.GetMonitorInfo(hMon, ref mi))
                    result.Add((hMon, mi.rcMonitor));
                return true; // continue enumeration
            }, IntPtr.Zero);

        // Primary first, then left-to-right, for stable display numbering.
        var ordered = result
            .Select(x => new { x.h, x.r, primary = IsPrimary(x.h) })
            .OrderByDescending(x => x.primary)
            .ThenBy(x => x.r.Left)
            .ToList();

        return ordered
            .Select((x, i) => new MonitorInfo(i, x.r.Left, x.r.Top, x.r.Width, x.r.Height, x.primary, x.h))
            .ToList();
    }

    /// <summary>The monitor the given window handle currently sits on.</summary>
    public static MonitorInfo? GetMonitorForWindow(IntPtr hwnd)
    {
        var target = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return GetMonitors().FirstOrDefault(m => m.Handle == target);
    }

    private static bool IsPrimary(IntPtr hMon)
    {
        var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        return NativeMethods.GetMonitorInfo(hMon, ref mi)
               && (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
    }
}
