using StreamOrchestrator.Display;
using StreamOrchestrator.Windows;

namespace StreamOrchestrator.Orchestration;

/// <summary>Something that can be placed fullscreen on a monitor: the stream, or a window.</summary>
public interface ISurface
{
    /// <summary>Stable identity used to detect the same surface across slots.</summary>
    object Key { get; }
    string Name { get; }
    void PlaceOn(MonitorInfo monitor);
}

/// <summary>The live RTSP stream (our borderless <see cref="StreamWindow"/>).</summary>
public sealed class StreamSurface : ISurface
{
    private readonly StreamWindow _window;
    public StreamSurface(StreamWindow window) => _window = window;

    public object Key => _window;
    public string Name => "● Live stream";
    public void PlaceOn(MonitorInfo monitor) => _window.ShowOnMonitor(monitor);
}

/// <summary>An external presentation/application window.</summary>
public sealed class WindowSurface : ISurface
{
    public IntPtr Handle { get; }
    public WindowSurface(IntPtr handle, string title) { Handle = handle; Name = title; }

    public object Key => Handle;
    public string Name { get; }
    public void PlaceOn(MonitorInfo monitor) => WindowMover.MoveToMonitor(Handle, monitor);
}
