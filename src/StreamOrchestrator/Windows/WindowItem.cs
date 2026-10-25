using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamOrchestrator.Windows;

/// <summary>Bindable row for the detected-windows list; survives auto-refresh so the user's
/// "track" selection is preserved while titles update.</summary>
public sealed class WindowItem : INotifyPropertyChanged
{
    public IntPtr Handle { get; }
    public string ProcessName { get; }

    private string _title;
    private bool _isTracked;

    public WindowItem(IntPtr handle, string title, string processName)
    {
        Handle = handle;
        _title = title;
        ProcessName = processName;
    }

    /// <summary>True when the owning app looks like a presentation/document/browser.</summary>
    public bool IsPresentationApp => PresentationFilter.IsPresentation(ProcessName);

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    /// <summary>User has marked this window for cycling.</summary>
    public bool IsTracked
    {
        get => _isTracked;
        set => Set(ref _isTracked, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
