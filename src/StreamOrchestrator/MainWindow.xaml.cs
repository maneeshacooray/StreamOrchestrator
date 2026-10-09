using System.Windows;
using StreamOrchestrator.Player;

namespace StreamOrchestrator;

/// <summary>
/// Interaction logic for MainWindow.xaml. Hosts the libmpv video surface and drives
/// play/stop of the RTSP stream.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MpvHost _host = new();
    private readonly MpvPlayer _player = new();

    public MainWindow()
    {
        InitializeComponent();

        _host.HostReady += OnHostReady;
        VideoContainer.Child = _host;

        Closing += (_, _) =>
        {
            _player.Dispose();
        };
    }

    private void OnHostReady(IntPtr hwnd)
    {
        // Runs on the UI thread as the native child window is created.
        Dispatcher.Invoke(() =>
        {
            try
            {
                _player.Initialize(hwnd);
                StatusText.Text = "Player ready. Enter an RTSP URL and press Play.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed to initialize player: " + ex.Message;
            }
        });
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url) || url == "rtsp://")
        {
            StatusText.Text = "Enter a stream URL first.";
            return;
        }

        try
        {
            _player.Open(url);
            StopButton.IsEnabled = true;
            StatusText.Text = "Playing: " + url;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Play failed: " + ex.Message;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _player.Stop();
            StopButton.IsEnabled = false;
            StatusText.Text = "Stopped.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Stop failed: " + ex.Message;
        }
    }
}
