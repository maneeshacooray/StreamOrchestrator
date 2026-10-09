using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
    private readonly DispatcherTimer _windowRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private StreamWindow? _stream;
    private StreamSurface? _streamSurface;
    private HotkeyService? _hotkeys;
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        WindowList.ItemsSource = _windowItems;
        _windowRefreshTimer.Tick += (_, _) => RefreshWindows();

        Loaded += (_, _) =>
        {
            LoadSettings();
            LoadMonitors();
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
    }

    private void SaveSettings()
    {
        _settings.LastUrl = UrlBox.Text?.Trim();
        _settings.LastDisplayIndex = MonitorCombo.SelectedIndex;
        _settings.StreamAlwaysOnTop = OnTopCheck.IsChecked == true;
        SettingsService.Save(_settings);
    }

    private void LoadMonitors()
    {
        _layout.RefreshMonitors();
        var monitors = MonitorService.GetMonitors();
        MonitorCombo.ItemsSource = monitors;
        MonitorCombo.DisplayMemberPath = nameof(MonitorInfo.Label);
        if (monitors.Count > 0 && MonitorCombo.SelectedIndex < 0)
        {
            var idx = _settings.LastDisplayIndex;
            MonitorCombo.SelectedIndex = idx >= 0 && idx < monitors.Count ? idx : 0;
        }

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
            else _windowItems.Add(new WindowItem(w.Handle, w.Title));
        }
        for (int i = _windowItems.Count - 1; i >= 0; i--)
            if (!liveHandles.Contains(_windowItems[i].Handle))
                _windowItems.RemoveAt(i);

        UpdateTracked();
    }

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
        StopButton.IsEnabled = false;
        StatusText.Text = "Stopped.";
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

    private void EnsureStreamWindow()
    {
        if (_stream is not null) return;
        _stream = new StreamWindow();
        _streamSurface = new StreamSurface(_stream);
        _stream.SetAlwaysOnTop(OnTopCheck.IsChecked == true);
        _stream.StatusChanged += status => StatusText.Text = status switch
        {
            Player.PlaybackStatus.Live => "● Live",
            Player.PlaybackStatus.Reconnecting => "Reconnecting…",
            Player.PlaybackStatus.Ended => "Stream ended.",
            _ => StatusText.Text,
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
