using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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

    private sealed record HotkeyAction(string Id, string Name, Action Invoke, uint DefaultMods, uint DefaultVk);
    private List<HotkeyAction> _hotkeyActions = null!;
    private readonly ObservableCollection<HotkeyRow> _hotkeyRows = new();
    private string? _capturingActionId;
    private string? _captureOldDisplay;

    public MainWindow()
    {
        InitializeComponent();
        _windowView = CollectionViewSource.GetDefaultView(_windowItems);
        _windowView.Filter = FilterWindow;
        WindowList.ItemsSource = _windowView;
        _windowRefreshTimer.Tick += (_, _) => RefreshWindows();

        BuildHotkeyActions();
        HotkeyList.ItemsSource = _hotkeyRows;

        Loaded += (_, _) =>
        {
            LoadSettings();
            LoadMonitors();
            ApplyPanelPreferences();
            RegisterHotkeys();
            RefreshHotkeyRows();
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
        _hotkeys = new HotkeyService(new WindowInteropHelper(this).Handle);
        // Actual bindings are registered in Loaded, once settings are read.
    }

    // ---- Configurable global hotkeys ----

    private void BuildHotkeyActions()
    {
        uint ca = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
        _hotkeyActions = new List<HotkeyAction>
        {
            new("swap",       "Swap displays",      () => SwapButton_Click(this, null!), ca, 0x53 /* S */),
            new("move",       "Move stream",        () => MoveButton_Click(this, null!), ca, 0x4D /* M */),
            new("stream-top", "Keep stream on top", ToggleAlwaysOnTop,                   ca, 0x54 /* T */),
            new("pin-panel",  "Pin control panel",  TogglePinPanel,                      ca, 0x50 /* P */),
        };
    }

    private Hotkey GetBinding(HotkeyAction a) =>
        _settings.Hotkeys.TryGetValue(a.Id, out var hk)
            ? hk
            : new Hotkey { Modifiers = a.DefaultMods, Vk = a.DefaultVk };

    /// <summary>Re-registers all hotkeys from current settings; returns actions the OS rejected.</summary>
    private List<string> RegisterHotkeys()
    {
        var failed = new List<string>();
        if (_hotkeys is null) return failed;
        _hotkeys.UnregisterAll();
        foreach (var a in _hotkeyActions)
        {
            var hk = GetBinding(a);
            if (hk.IsSet && !_hotkeys.Register(hk.Modifiers, hk.Vk, a.Invoke))
                failed.Add(a.Name);
        }
        return failed;
    }

    private void RefreshHotkeyRows()
    {
        _hotkeyRows.Clear();
        foreach (var a in _hotkeyActions)
        {
            var hk = GetBinding(a);
            _hotkeyRows.Add(new HotkeyRow(a.Id, a.Name, HotkeyFormat.Describe(hk.Modifiers, hk.Vk)));
        }
    }

    private void SetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HotkeyRow row }) return;
        CancelCapture();
        _capturingActionId = row.Id;
        _captureOldDisplay = row.Display;
        row.Capturing = true;
        row.Display = "Press a key combo…  (Esc to cancel)";
        StatusText.Text = "Listening for a hotkey… hold modifiers (Ctrl/Alt/Shift/Win) and press a key.";
        Focus();
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: HotkeyRow row }) return;
        CancelCapture();
        _settings.Hotkeys[row.Id] = new Hotkey { Modifiers = 0, Vk = 0 };
        row.Display = HotkeyFormat.Describe(0, 0);
        RegisterHotkeys();
        StatusText.Text = $"Cleared hotkey for {row.Name}.";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingActionId is null) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { CancelCapture(); e.Handled = true; return; }
        if (HotkeyFormat.IsModifierKey(key)) { e.Handled = true; return; } // wait for a real key

        var mods = HotkeyFormat.ToWin32Modifiers(Keyboard.Modifiers);
        if (mods == 0)
        {
            StatusText.Text = "Use at least one modifier (Ctrl / Alt / Shift / Win).";
            e.Handled = true;
            return;
        }

        var vk = HotkeyFormat.ToVirtualKey(key);
        var row = _hotkeyRows.First(r => r.Id == _capturingActionId);
        _settings.Hotkeys[_capturingActionId] = new Hotkey { Modifiers = mods, Vk = vk };
        row.Display = HotkeyFormat.Describe(mods, vk);
        row.Capturing = false;
        _capturingActionId = null;
        e.Handled = true;

        var failed = RegisterHotkeys();
        StatusText.Text = failed.Contains(row.Name)
            ? $"“{row.Display}” is already in use by another app — pick another."
            : $"Set {row.Name} to {row.Display}.";
    }

    private void CancelCapture()
    {
        if (_capturingActionId is null) return;
        var row = _hotkeyRows.FirstOrDefault(r => r.Id == _capturingActionId);
        if (row is not null)
        {
            row.Capturing = false;
            if (_captureOldDisplay is not null) row.Display = _captureOldDisplay;
        }
        _capturingActionId = null;
        _captureOldDisplay = null;
    }

    private void LoadSettings()
    {
        _settings = SettingsService.Load();
        if (!string.IsNullOrWhiteSpace(_settings.LastUrl)) UrlBox.Text = _settings.LastUrl;
        StreamOnTopToggle.IsChecked = _settings.StreamAlwaysOnTop;
        PresOnlyCheck.IsChecked = _settings.PresentationsOnly;
        _windowView.Refresh();
    }

    private void SaveSettings()
    {
        _settings.LastUrl = UrlBox.Text?.Trim();
        _settings.LastDisplayIndex = MonitorCombo.SelectedIndex;
        _settings.StreamAlwaysOnTop = StreamOnTopToggle.IsChecked == true;
        _settings.PresentationsOnly = PresOnlyCheck.IsChecked == true;
        _settings.PinControlPanelOnTop = PinPanelToggle.IsChecked == true;
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

    private void StreamOnTopToggle_Click(object sender, RoutedEventArgs e) => ApplyAlwaysOnTop();

    private void ToggleAlwaysOnTop()
    {
        StreamOnTopToggle.IsChecked = StreamOnTopToggle.IsChecked != true;
        ApplyAlwaysOnTop();
    }

    private void ApplyAlwaysOnTop()
    {
        bool onTop = StreamOnTopToggle.IsChecked == true;
        _stream?.SetAlwaysOnTop(onTop);
        StatusText.Text = onTop ? "Stream kept on top." : "Stream on-top disabled.";
    }

    private void RefreshDisplaysButton_Click(object sender, RoutedEventArgs e) => LoadMonitors();

    private void RefreshWindowsButton_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    // ---- Control-panel window: pin on top + bind to a display ----

    private void ApplyPanelPreferences()
    {
        PinPanelToggle.IsChecked = _settings.PinControlPanelOnTop;
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

    private void PinPanelToggle_Click(object sender, RoutedEventArgs e) => ApplyPinPanel();

    private void TogglePinPanel()
    {
        PinPanelToggle.IsChecked = PinPanelToggle.IsChecked != true;
        ApplyPinPanel();
    }

    private void ApplyPinPanel()
    {
        Topmost = PinPanelToggle.IsChecked == true;
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
        if (StreamPanel is null || DisplaysPanel is null || PresentationsPanel is null || HotkeysPanel is null) return;
        if (_capturingActionId is not null) CancelCapture();
        StreamPanel.Visibility = NavStream.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DisplaysPanel.Visibility = NavDisplays.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PresentationsPanel.Visibility = NavPresentations.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HotkeysPanel.Visibility = NavHotkeys.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
        _stream.SetAlwaysOnTop(StreamOnTopToggle.IsChecked == true);
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
