using System.Windows;
using StreamOrchestrator.Display;

namespace StreamOrchestrator;

/// <summary>
/// Control panel: owns the stream URL, display selection, and the borderless <see cref="StreamWindow"/>
/// that renders the live feed. Playback and window orchestration are driven from here.
/// </summary>
public partial class MainWindow : Window
{
    private StreamWindow? _stream;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadMonitors();
        Closing += (_, _) => _stream?.ShutDown();
    }

    private void LoadMonitors()
    {
        var monitors = MonitorService.GetMonitors();
        MonitorCombo.ItemsSource = monitors;
        MonitorCombo.DisplayMemberPath = nameof(MonitorInfo.Label);
        if (monitors.Count > 0) MonitorCombo.SelectedIndex = 0;

        MoveButton.IsEnabled = monitors.Count >= 2 && _stream is not null;
        StatusText.Text = monitors.Count >= 2
            ? $"{monitors.Count} displays detected."
            : "Single display detected — Move is disabled.";
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
            _stream!.ShowOnMonitor(monitor);
            _stream.Open(url);

            StopButton.IsEnabled = true;
            MoveButton.IsEnabled = MonitorService.GetMonitors().Count >= 2;
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
        if (_stream is null) return;
        var moved = _stream.MoveToOtherDisplay();
        if (moved is not null)
        {
            // Reflect the new display in the combo.
            var match = (MonitorCombo.ItemsSource as IEnumerable<MonitorInfo>)?
                .FirstOrDefault(m => m.Handle == moved.Handle);
            if (match is not null) MonitorCombo.SelectedItem = match;
            StatusText.Text = $"Moved to {moved.Label}";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadMonitors();

    private void EnsureStreamWindow()
    {
        if (_stream is not null) return;
        _stream = new StreamWindow { Owner = null };
        _stream.Closed += (_, _) =>
        {
            _stream = null;
            StopButton.IsEnabled = false;
            MoveButton.IsEnabled = false;
        };
    }
}
