using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using StreamOrchestrator.Config;
using StreamOrchestrator.Display;
using StreamOrchestrator.Input;
using StreamOrchestrator.Interop;
using StreamOrchestrator.Orchestration;
using StreamOrchestrator.Windows;

namespace StreamOrchestrator;

/// <summary>
/// Control panel: owns the stream URL, display selection, the borderless <see cref="StreamWindow"/>,
/// and the <see cref="LayoutController"/> that moves/swaps surfaces between the two displays.
/// </summary>
public partial class MainWindow : Window
{
    private readonly LayoutController _layout = new();
    private readonly ObservableCollection<WindowItem> _windowItems = new();
    private readonly ICollectionView _windowView;
    private readonly DispatcherTimer _windowRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private StreamWindow? _stream;
    private StreamSurface? _streamSurface;
    private HotkeyService? _hotkeys;
    private AppSettings _settings = new();
    private bool _suppressAppMonitorChange;

    public MainWindow()
    {
        InitializeComponent();
        _windowView = CollectionViewSource.GetDefaultView(_windowItems);
        _windowView.Filter = FilterWindow;
        WindowList.ItemsSource = _windowView;
        _windowRefreshTimer.Tick += (_, _) => RefreshWindows();

        Loaded += (_, _) =>
        {
            LoadSettings();
            LoadMonitors();
            ApplyPanelPreferences();
            RefreshWindows();
            _windowRefreshTimer.Start();
        };
        Closing += (_, _) =>
        {
            _windowRefreshTimer.Stop();
            SaveSettings();
            _layout.RestoreWindows();
            _hotkeys?.Dispose();
            _stream?.ShutDown();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeys = new HotkeyService(hwnd);
        uint ctrlAlt = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
        _hotkeys.Register(ctrlAlt, 0x53 /* S */, () => SwapButton_Click(this, null!));
        _hotkeys.Register(ctrlAlt, 0x4D /* M */, () => MoveButton_Click(this, null!));
        _hotkeys.Register(ctrlAlt, 0x54 /* T */, ToggleAlwaysOnTop);
    }

    private void LoadSettings()
    {
        _settings = SettingsService.Load();
        if (!string.IsNullOrWhiteSpace(_settings.LastUrl)) UrlBox.Text = _settings.LastUrl;
        OnTopCheck.IsChecked = _settings.StreamAlwaysOnTop;
        PresOnlyCheck.IsChecked = _settings.PresentationsOnly;
        _windowView.Refresh();
    }

    private void SaveSettings()
    {
        _settings.LastUrl = UrlBox.Text?.Trim();
        _settings.LastDisplayIndex = MonitorCombo.SelectedIndex;
        _settings.StreamAlwaysOnTop = OnTopCheck.IsChecked == true;
        _settings.PresentationsOnly = PresOnlyCheck.IsChecked == true;
        _settings.PinControlPanelOnTop = PinPanelCheck.IsChecked == true;
        _settings.AppDisplayIndex = AppMonitorCombo.SelectedIndex;
        SettingsService.Save(_settings);
    }

    private void LoadMonitors()
    {
        _layout.RefreshMonitors();
        var monitors = MonitorService.GetMonitors();
        MonitorCombo.ItemsSource = monitors;
        if (monitors.Count > 0 && MonitorCombo.SelectedIndex < 0)
        {
            var idx = _settings.LastDisplayIndex;
            MonitorCombo.SelectedIndex = idx >= 0 && idx < monitors.Count ? idx : 0;
        }

        // Populate the "Show on" combo, reflecting where this window currently sits.
        _suppressAppMonitorChange = true;
        AppMonitorCombo.ItemsSource = monitors;
        AppMonitorCombo.SelectedIndex = monitors.Count > 0 ? IndexOfCurrentAppMonitor(monitors) : -1;
        _suppressAppMonitorChange = false;

        bool multi = monitors.Count >= 2;
        SwapButton.IsEnabled = multi;
        MoveButton.IsEnabled = multi && _stream is not null;
        StatusText.Text = multi
            ? $"{monitors.Count} displays detected."
            : "Single display detected — move/swap disabled.";
    }

    private void RefreshWindows()
    {
        var live = WindowEnumerator.GetWindows();
        var liveHandles = live.Select(w => w.Handle).ToHashSet();
        var existing = _windowItems.ToDictionary(i => i.Handle);

        foreach (var w in live)
        {
            if (existing.TryGetValue(w.Handle, out var item)) item.Title = w.Title;
            else _windowItems.Add(new WindowItem(w.Handle, w.Title, w.ProcessName));
        }
        for (int i = _windowItems.Count - 1; i >= 0; i--)
            if (!liveHandles.Contains(_windowItems[i].Handle))
                _windowItems.RemoveAt(i);

        UpdateTracked();
    }

    private bool FilterWindow(object obj)
    {
        if (obj is not WindowItem w) return false;
        if (PresOnlyCheck?.IsChecked == true && !w.IsPresentationApp) return false;
        var q = SearchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(q) &&
            w.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _windowView.Refresh();

    private void PresOnlyCheck_Click(object sender, RoutedEventArgs e) => _windowView.Refresh();

    private void UpdateTracked()
    {
        var tracked = _windowItems.Where(i => i.IsTracked).Select(i => new WindowSurface(i.Handle, i.Title)).ToList();
        _layout.SetTracked(tracked);
        bool canCycle = tracked.Count > 0;
        PrevButton.IsEnabled = canCycle;
        NextButton.IsEnabled = canCycle;
        RestoreButton.IsEnabled = WindowMover.HasRestorable;
    }

    /// <summary>The display presentations cycle onto: the one not showing the stream.</summary>
    private int CycleTargetDisplay()
    {
        if (_layout.MonitorCount < 2) return 0;
        return _layout.StreamDisplayIndex == 1 ? 0 : 1;
    }

    private MonitorInfo? SelectedMonitor => MonitorCombo.SelectedItem as MonitorInfo;

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url) || url == "rtsp://")
        {
            StatusText.Text = "Enter a stream URL first.";
            return;
        }

        var monitor = SelectedMonitor ?? MonitorService.GetMonitors().FirstOrDefault();
        if (monitor is null)
        {
            StatusText.Text = "No display available.";
            return;
        }

        try
        {
            EnsureStreamWindow();
            _layout.AssignToDisplay(monitor.Index, _streamSurface!);
            _stream!.Open(url);

            StopButton.IsEnabled = true;
            MoveButton.IsEnabled = _layout.MonitorCount >= 2;
            StatusText.Text = $"Playing on {monitor.Label}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Play failed: " + ex.Message;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _stream?.Stop();
        _stream?.Hide();        // remove the fullscreen surface from the display
        StopButton.IsEnabled = false;
        MoveButton.IsEnabled = false;
        StatusText.Text = "Stopped.";
        SetLive("Idle", IdleBrush);
    }

    private void MoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_streamSurface is null) return;
        if (_layout.MoveSurfaceToOtherDisplay(_streamSurface.Key))
            StatusText.Text = "Moved the stream to the other display.";
    }

    private void SwapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_layout.Swap())
            StatusText.Text = "Swapped the two displays.";
    }

    private void SendWindow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: WindowItem item, Tag: string tag }) return;
        if (!int.TryParse(tag, out var displayIndex)) return;

        if (_layout.DisplayAt(displayIndex) is null)
        {
            StatusText.Text = $"Display {displayIndex + 1} is not available.";
            return;
        }

        _layout.AssignToDisplay(displayIndex, new WindowSurface(item.Handle, item.Title));
        RestoreButton.IsEnabled = WindowMover.HasRestorable;
        StatusText.Text = $"Sent \"{item.Title}\" to Display {displayIndex + 1}.";
    }

    private void TrackChanged_Click(object sender, RoutedEventArgs e) => UpdateTracked();

    private void PrevButton_Click(object sender, RoutedEventArgs e) => Cycle(-1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => Cycle(+1);

    private void Cycle(int direction)
    {
        var surface = _layout.CycleOnDisplay(CycleTargetDisplay(), direction);
        if (surface is null)
        {
            StatusText.Text = "Track one or more presentation windows first.";
            return;
        }
        RestoreButton.IsEnabled = WindowMover.HasRestorable;
        StatusText.Text = $"Showing \"{surface.Name}\" on Display {CycleTargetDisplay() + 1}.";
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        _layout.RestoreWindows();
        RestoreButton.IsEnabled = false;
        StatusText.Text = "Restored moved windows.";
    }

    private void OnTopCheck_Click(object sender, RoutedEventArgs e) => ApplyAlwaysOnTop();

    private void ToggleAlwaysOnTop()
    {
        OnTopCheck.IsChecked = OnTopCheck.IsChecked != true;
        ApplyAlwaysOnTop();
    }

    private void ApplyAlwaysOnTop()
    {
        bool onTop = OnTopCheck.IsChecked == true;
        _stream?.SetAlwaysOnTop(onTop);
        StatusText.Text = onTop ? "Stream kept on top." : "Stream on-top disabled.";
    }

    private void RefreshDisplaysButton_Click(object sender, RoutedEventArgs e) => LoadMonitors();

    private void RefreshWindowsButton_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    // ---- Control-panel window: pin on top + bind to a display ----

    private void ApplyPanelPreferences()
    {
        PinPanelCheck.IsChecked = _settings.PinControlPanelOnTop;
        Topmost = _settings.PinControlPanelOnTop;

        if (AppMonitorCombo.ItemsSource is IReadOnlyList<MonitorInfo> monitors
            && _settings.AppDisplayIndex >= 0 && _settings.AppDisplayIndex < monitors.Count)
        {
            _suppressAppMonitorChange = true;
            AppMonitorCombo.SelectedIndex = _settings.AppDisplayIndex;
            _suppressAppMonitorChange = false;
            MoveAppToMonitor(monitors[_settings.AppDisplayIndex]);
        }
    }

    private void PinPanelCheck_Click(object sender, RoutedEventArgs e)
    {
        Topmost = PinPanelCheck.IsChecked == true;
        StatusText.Text = Topmost ? "Control panel pinned on top." : "Control panel unpinned.";
    }

    private void AppMonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAppMonitorChange) return;
        if (AppMonitorCombo.SelectedItem is MonitorInfo m)
        {
            MoveAppToMonitor(m);
            StatusText.Text = $"Control panel moved to {m.Label}.";
        }
    }

    private void MoveAppToMonitor(MonitorInfo m)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Top-left of the monitor's work area with a small margin; keep the current size.
        const int margin = 48;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            m.WorkLeft + margin, m.WorkTop + margin, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private int IndexOfCurrentAppMonitor(IReadOnlyList<MonitorInfo> monitors)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var current = hwnd != IntPtr.Zero ? MonitorService.GetMonitorForWindow(hwnd) : null;
        if (current is null) return 0;
        for (int i = 0; i < monitors.Count; i++)
            if (monitors[i].Handle == current.Handle) return i;
        return 0;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // Can fire during XAML parse before the panels exist; guard until they do.
        if (StreamPanel is null || DisplaysPanel is null || PresentationsPanel is null) return;
        StreamPanel.Visibility = NavStream.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DisplaysPanel.Visibility = NavDisplays.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PresentationsPanel.Visibility = NavPresentations.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private static readonly System.Windows.Media.Brush LiveBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0xD4, 0xBF));
    private static readonly System.Windows.Media.Brush WarnBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xA6, 0x23));
    private static readonly System.Windows.Media.Brush IdleBrush =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x79));

    private void SetLive(string text, System.Windows.Media.Brush brush)
    {
        LiveText.Text = text;
        LiveDot.Fill = brush;
    }

    private void EnsureStreamWindow()
    {
        if (_stream is not null) return;
        _stream = new StreamWindow();
        _streamSurface = new StreamSurface(_stream);
        _stream.SetAlwaysOnTop(OnTopCheck.IsChecked == true);
        _stream.StatusChanged += status =>
        {
            switch (status)
            {
                case Player.PlaybackStatus.Live:
                    StatusText.Text = "Live"; SetLive("Live", LiveBrush); break;
                case Player.PlaybackStatus.Reconnecting:
                    StatusText.Text = "Reconnecting…"; SetLive("Reconnecting", WarnBrush); break;
                case Player.PlaybackStatus.Ended:
                    StatusText.Text = "Stream ended."; SetLive("Idle", IdleBrush); break;
            }
        };
        _stream.Closed += (_, _) =>
        {
            _stream = null;
            _streamSurface = null;
            StopButton.IsEnabled = false;
            MoveButton.IsEnabled = false;
        };
    }
}
