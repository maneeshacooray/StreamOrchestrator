using System.Text;
using StreamOrchestrator.Interop;

namespace StreamOrchestrator.Windows;

/// <summary>An external top-level window the user can place on a display.</summary>
public sealed record WindowInfo(IntPtr Handle, string Title);

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

            windows.Add(new WindowInfo(hwnd, title));
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
