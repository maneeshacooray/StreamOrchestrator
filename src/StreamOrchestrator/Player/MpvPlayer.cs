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
}

/// <summary>
/// Wraps a single libmpv instance configured for minimum-latency live playback and bound to a
/// native host window.
/// </summary>
public sealed class MpvPlayer : IDisposable
{
    private IntPtr _ctx;
    private bool _initialized;

    public bool IsInitialized => _initialized;

    /// <summary>Creates the mpv context, applies low-latency options, binds it to <paramref name="hwnd"/>.</summary>
    public void Initialize(IntPtr hwnd, PlayerOptions? options = null)
    {
        if (_initialized) return;
        options ??= new PlayerOptions();

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
        MpvInterop.SetOptionString(_ctx, "rtsp-transport", options.RtspTransport);
        MpvInterop.SetOptionString(_ctx, "hwdec", options.HardwareDecode);
        MpvInterop.SetOptionString(_ctx, "audio", options.Audio ? "auto" : "no");
        MpvInterop.SetOptionString(_ctx, "keepaspect", "yes");
        MpvInterop.SetOptionString(_ctx, "idle", "yes");
        MpvInterop.SetOptionString(_ctx, "force-window", "no");
        MpvInterop.SetOptionString(_ctx, "osc", "no");
        MpvInterop.SetOptionString(_ctx, "input-default-bindings", "no");
        MpvInterop.SetOptionString(_ctx, "input-vo-keyboard", "no");

        MpvInterop.mpv_initialize(_ctx);
        _initialized = true;
    }

    /// <summary>Starts (or replaces) the stream at <paramref name="url"/>, e.g. an rtsp:// URL.</summary>
    public void Open(string url)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Stream URL is empty.", nameof(url));
        MpvInterop.Command(_ctx, "loadfile", url, "replace");
    }

    /// <summary>Stops playback and clears the current file.</summary>
    public void Stop()
    {
        if (!_initialized) return;
        MpvInterop.Command(_ctx, "stop");
    }

    private void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("MpvPlayer is not initialized.");
    }

    public void Dispose()
    {
        if (_ctx != IntPtr.Zero)
        {
            MpvInterop.mpv_terminate_destroy(_ctx);
            _ctx = IntPtr.Zero;
        }
        _initialized = false;
    }
}
