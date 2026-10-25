using System.Diagnostics;
using System.Text;
using StreamOrchestrator.Interop;

namespace StreamOrchestrator.Windows;

/// <summary>An external top-level window the user can place on a display.</summary>
public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName);

/// <summary>
/// Lists candidate "presentation" windows: visible, titled, top-level windows belonging to other
/// processes (PowerPoint, a PDF viewer, a browser, …).
/// </summary>
public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> GetWindows()
    {
        var ownPid = (uint)Environment.ProcessId;
        var windows = new List<WindowInfo>();
        var sb = new StringBuilder(512);
        var nameByPid = new Dictionary<uint, string>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;

            var len = NativeMethods.GetWindowTextLength(hwnd);
            if (len == 0) return true;

            // Skip tool windows (tooltips, palettes, etc.).
            var exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            // Skip our own windows.
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownPid) return true;

            sb.Clear();
            if (sb.Capacity < len + 1) sb.Capacity = len + 1;
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            windows.Add(new WindowInfo(hwnd, title, GetProcessName(pid, nameByPid)));
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string GetProcessName(uint pid, Dictionary<uint, string> cache)
    {
        if (cache.TryGetValue(pid, out var name)) return name;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            name = proc.ProcessName; // without ".exe"
        }
        catch
        {
            name = string.Empty;
        }
        cache[pid] = name;
        return name;
    }
}
