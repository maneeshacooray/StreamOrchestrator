using System.Runtime.InteropServices;
using System.Threading;

namespace StreamOrchestrator.Player;

/// <summary>Tunables that trade latency against robustness for a live RTSP feed.</summary>
public sealed class PlayerOptions
{
    /// <summary>RTSP transport: "tcp" (reliable, default) or "udp" (sometimes lower latency).</summary>
    public string RtspTransport { get; init; } = "tcp";

    /// <summary>Hardware decoder selection passed to mpv's <c>hwdec</c>.</summary>
    public string HardwareDecode { get; init; } = "auto-safe";

    /// <summary>Play audio from the stream. CCTV feeds usually have none; off reduces overhead.</summary>
    public bool Audio { get; init; } = false;

    /// <summary>Reconnect automatically when the live stream drops (EOF/error).</summary>
    public bool AutoReconnect { get; init; } = true;

    /// <summary>Delay before each reconnect attempt.</summary>
    public int ReconnectDelayMs { get; init; } = 2000;
}

/// <summary>Playback state surfaced to the UI.</summary>
public enum PlaybackStatus { Live, Reconnecting, Ended }

/// <summary>
/// Wraps a single libmpv instance configured for minimum-latency live playback and bound to a
/// native host window. Runs a background event loop that auto-reconnects when the feed drops.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private IntPtr _ctx;
    private bool _initialized;
    private PlayerOptions _options = new();

    private Thread? _eventThread;
    private volatile bool _running;
    private volatile bool _manualStop;
    private volatile string? _lastUrl;

    public bool IsInitialized => _initialized;

    /// <summary>Raised from a background thread when playback status changes; marshal to the UI.</summary>
    public event Action<PlaybackStatus>? StatusChanged;

    /// <summary>Creates the mpv context, applies low-latency options, binds it to <paramref name="hwnd"/>.</summary>
    public void Initialize(IntPtr hwnd, PlayerOptions? options = null)
    {
        if (_initialized) return;
        _options = options ?? new PlayerOptions();

        _ctx = MpvInterop.mpv_create();
        if (_ctx == IntPtr.Zero)
            throw new MpvException("mpv_create failed (is libmpv-2.dll present next to the executable?).");

        // Embed: render into our host window instead of creating a top-level one.
        MpvInterop.SetOptionInt64(_ctx, "wid", hwnd.ToInt64());

        // --- Low-latency profile (the core of the latency requirement) ---
        MpvInterop.SetOptionString(_ctx, "profile", "low-latency");
        MpvInterop.SetOptionString(_ctx, "untimed", "yes");
        MpvInterop.SetOptionString(_ctx, "cache", "no");
        MpvInterop.SetOptionString(_ctx, "cache-pause", "no");
        MpvInterop.SetOptionString(_ctx, "demuxer-lavf-o", "fflags=nobuffer");
        MpvInterop.SetOptionString(_ctx, "video-latency-hacks", "yes");
        MpvInterop.SetOptionString(_ctx, "interpolation", "no");
        // Frame-threaded decoding reorders output and adds latency; one thread avoids that.
        MpvInterop.SetOptionString(_ctx, "vd-lavc-threads", "1");

        // --- Transport / decode / display ---
        MpvInterop.SetOptionString(_ctx, "rtsp-transport", _options.RtspTransport);
        MpvInterop.SetOptionString(_ctx, "hwdec", _options.HardwareDecode);
        MpvInterop.SetOptionString(_ctx, "audio", _options.Audio ? "auto" : "no");
        MpvInterop.SetOptionString(_ctx, "keepaspect", "yes");
        MpvInterop.SetOptionString(_ctx, "idle", "yes");
        MpvInterop.SetOptionString(_ctx, "force-window", "no");
        MpvInterop.SetOptionString(_ctx, "osc", "no");
        MpvInterop.SetOptionString(_ctx, "input-default-bindings", "no");
        MpvInterop.SetOptionString(_ctx, "input-vo-keyboard", "no");

        MpvInterop.mpv_initialize(_ctx);
        _initialized = true;

        _running = true;
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _eventThread.Start();
    }

    /// <summary>Starts (or replaces) the stream at <paramref name="url"/>, e.g. an rtsp:// URL.</summary>
    public void Open(string url)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Stream URL is empty.", nameof(url));
        _manualStop = false;
        _lastUrl = url;
        MpvInterop.Command(_ctx, "loadfile", url, "replace");
    }

    /// <summary>Stops playback and clears the current file (disables reconnect until next Open).</summary>
    public void Stop()
    {
        if (!_initialized) return;
        _manualStop = true;
        _lastUrl = null;
        MpvInterop.Command(_ctx, "stop");
    }

    private void EventLoop()
    {
        while (_running)
        {
            var p = MpvInterop.mpv_wait_event(_ctx, 0.1);
            if (p == IntPtr.Zero) continue;

            var ev = Marshal.PtrToStructure<MpvInterop.MpvEvent>(p);
            switch (ev.event_id)
            {
                case MpvInterop.MPV_EVENT_SHUTDOWN:
                    _running = false;
                    break;

                case MpvInterop.MPV_EVENT_FILE_LOADED:
                    StatusChanged?.Invoke(PlaybackStatus.Live);
                    break;

                case MpvInterop.MPV_EVENT_END_FILE:
                    HandleEndFile(ev.data);
                    break;
            }
        }
    }

    private void HandleEndFile(IntPtr data)
    {
        int reason = data != IntPtr.Zero
            ? Marshal.PtrToStructure<MpvInterop.MpvEventEndFile>(data).reason
            : -1;

        bool unexpected = reason is MpvInterop.MPV_END_FILE_REASON_EOF
                                 or MpvInterop.MPV_END_FILE_REASON_ERROR;

        var url = _lastUrl;
        if (!_options.AutoReconnect || _manualStop || !unexpected || url is null)
        {
            StatusChanged?.Invoke(PlaybackStatus.Ended);
            return;
        }

        StatusChanged?.Invoke(PlaybackStatus.Reconnecting);
        Thread.Sleep(_options.ReconnectDelayMs);
        if (_running && !_manualStop && _lastUrl == url)
            MpvInterop.Command(_ctx, "loadfile", url, "replace");
    }

    private void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("MpvPlayer is not initialized.");
    }

    public void Dispose()
    {
        _running = false;
        _manualStop = true;
        _eventThread?.Join(TimeSpan.FromMilliseconds(500));
        if (_ctx != IntPtr.Zero)
        {
            MpvInterop.mpv_terminate_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
        _initialized = false;
    }
}
