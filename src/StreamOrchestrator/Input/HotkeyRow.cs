using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StreamOrchestrator.Input;

/// <summary>Bindable row for the Hotkeys settings list.</summary>
public sealed class HotkeyRow : INotifyPropertyChanged
{
    public string Id { get; }
    public string Name { get; }

    private string _display;
    private bool _capturing;

    public HotkeyRow(string id, string name, string display)
    {
        Id = id;
        Name = name;
        _display = display;
    }

    /// <summary>Human-readable current binding, e.g. "Ctrl + Alt + S".</summary>
    public string Display
    {
        get => _display;
        set => Set(ref _display, value);
    }

    /// <summary>True while waiting for the user to press a new combo.</summary>
    public bool Capturing
    {
        get => _capturing;
        set => Set(ref _capturing, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
