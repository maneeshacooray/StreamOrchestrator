using StreamOrchestrator.Display;
using StreamOrchestrator.Windows;

namespace StreamOrchestrator.Orchestration;

/// <summary>
/// Tracks which surface occupies each of the (up to two) monitors and performs move/swap
/// operations. Slot <c>i</c> corresponds to <see cref="MonitorService"/> display <c>i</c>.
/// </summary>
public sealed class LayoutController
{
    private readonly ISurface?[] _slots = new ISurface?[2];
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();

    // Ordered presentations the user is cycling through, and the current position.
    private readonly List<WindowSurface> _tracked = new();
    private int _cycleIndex = -1;

    public LayoutController() => RefreshMonitors();

    public int MonitorCount => _monitors.Count;

    /// <summary>Index of the display currently showing the stream, or -1.</summary>
    public int StreamDisplayIndex => Array.FindIndex(_slots, s => s is StreamSurface);

    public void RefreshMonitors() => _monitors = MonitorService.GetMonitors();

    /// <summary>Places a surface on the given display, removing it from any other slot first.</summary>
    public void AssignToDisplay(int displayIndex, ISurface surface)
    {
        if (displayIndex < 0 || displayIndex >= _monitors.Count) return;

        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i] is not null && Equals(_slots[i]!.Key, surface.Key))
                _slots[i] = null;

        _slots[displayIndex] = surface;
        surface.PlaceOn(_monitors[displayIndex]);
    }

    /// <summary>Exchanges the contents of the two displays and repositions both.</summary>
    public bool Swap()
    {
        if (_monitors.Count < 2) return false;
        (_slots[0], _slots[1]) = (_slots[1], _slots[0]);
        Reflow();
        return true;
    }

    /// <summary>Moves the stream surface to the other display, swapping with whatever is there.</summary>
    public bool MoveSurfaceToOtherDisplay(object key)
    {
        if (_monitors.Count < 2) return false;
        int idx = Array.FindIndex(_slots, s => s is not null && Equals(s.Key, key));
        if (idx < 0) return false;
        return Swap();
    }

    /// <summary>Re-applies the current slot assignments (e.g. after displays change).</summary>
    public void Reflow()
    {
        for (int i = 0; i < _slots.Length && i < _monitors.Count; i++)
            _slots[i]?.PlaceOn(_monitors[i]);
    }

    public MonitorInfo? DisplayAt(int index) =>
        index >= 0 && index < _monitors.Count ? _monitors[index] : null;

    /// <summary>Sets the ordered list of presentations to cycle through.</summary>
    public void SetTracked(IReadOnlyList<WindowSurface> tracked)
    {
        _tracked.Clear();
        _tracked.AddRange(tracked);
        if (_cycleIndex >= _tracked.Count) _cycleIndex = _tracked.Count - 1;
    }

    public int TrackedCount => _tracked.Count;

    /// <summary>Shows the next/previous tracked presentation on the given display.</summary>
    public WindowSurface? CycleOnDisplay(int displayIndex, int direction)
    {
        if (_tracked.Count == 0 || displayIndex < 0 || displayIndex >= _monitors.Count) return null;

        _cycleIndex = _cycleIndex < 0
            ? (direction >= 0 ? 0 : _tracked.Count - 1)
            : ((_cycleIndex + direction) % _tracked.Count + _tracked.Count) % _tracked.Count;

        var surface = _tracked[_cycleIndex];
        AssignToDisplay(displayIndex, surface);
        return surface;
    }

    /// <summary>Restores every moved presentation window to where it was before.</summary>
    public void RestoreWindows() => WindowMover.RestoreAll();
}
