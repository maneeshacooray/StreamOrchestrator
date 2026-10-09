using System.Windows;
using System.Windows.Interop;
using StreamOrchestrator.Interop;
using StreamOrchestrator.Player;

namespace StreamOrchestrator.Display;

/// <summary>
/// Borderless window that carries the libmpv video surface and can be placed fullscreen on any
/// monitor. This is the movable/swappable "stream" surface.
/// </summary>
public partial class StreamWindow : Window
{
    private readonly MpvHost _host = new();
    private readonly MpvPlayer _player = new();
    private bool _playerReady;
    private string? _pendingUrl;

    /// <summary>Handle of the monitor the window currently occupies (updated on each placement).</summary>
    public IntPtr CurrentMonitorHandle { get; private set; }

    public StreamWindow()
    {
        InitializeComponent();
        _host.HostReady += OnHostReady;
        VideoContainer.Children.Add(_host);
    }

    private void OnHostReady(IntPtr hwnd)
    {
        Dispatcher.Invoke(() =>
        {
            _player.Initialize(hwnd);
            _playerReady = true;
            if (_pendingUrl is not null)
            {
                _player.Open(_pendingUrl);
                _pendingUrl = null;
            }
        });
    }

    /// <summary>Starts (or replaces) playback; queues until the player is initialized.</summary>
    public void Open(string url)
    {
        if (_playerReady) _player.Open(url);
        else _pendingUrl = url;
    }

    public void Stop()
    {
        _pendingUrl = null;
        if (_playerReady) _player.Stop();
    }

    /// <summary>Places the window fullscreen (borderless) on the given monitor.</summary>
    public void ShowOnMonitor(MonitorInfo monitor)
    {
        if (!IsVisible) Show();

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
            monitor.Left, monitor.Top, monitor.Width, monitor.Height,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

        CurrentMonitorHandle = monitor.Handle;
    }

    /// <summary>Moves the stream to the other display (dual-monitor setup).</summary>
    public MonitorInfo? MoveToOtherDisplay()
    {
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count < 2) return null;

        var other = monitors.FirstOrDefault(m => m.Handle != CurrentMonitorHandle)
                    ?? monitors[0];
        ShowOnMonitor(other);
        return other;
    }

    public void ShutDown()
    {
        _player.Dispose();
        Close();
    }
}
