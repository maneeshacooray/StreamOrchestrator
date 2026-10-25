using System.Windows.Input;
using StreamOrchestrator.Interop;

namespace StreamOrchestrator.Input;

/// <summary>Converts between WPF key input and Win32 hotkey values, and formats them for display.</summary>
public static class HotkeyFormat
{
    /// <summary>Maps WPF modifier keys to Win32 MOD_* flags.</summary>
    public static uint ToWin32Modifiers(ModifierKeys mods)
    {
        uint m = 0;
        if (mods.HasFlag(ModifierKeys.Control)) m |= NativeMethods.MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Alt)) m |= NativeMethods.MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Shift)) m |= NativeMethods.MOD_SHIFT;
        if (mods.HasFlag(ModifierKeys.Windows)) m |= 0x0008; // MOD_WIN
        return m;
    }

    public static uint ToVirtualKey(Key key) => (uint)KeyInterop.VirtualKeyFromKey(key);

    /// <summary>True for keys that are modifiers themselves (not a valid hotkey trigger alone).</summary>
    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    public static string Describe(uint modifiers, uint vk)
    {
        if (vk == 0) return "Not set";

        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        parts.Add(KeyName(vk));
        return string.Join(" + ", parts);
    }

    private static string KeyName(uint vk)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        var s = key.ToString();
        if (s.Length == 2 && s[0] == 'D' && char.IsDigit(s[1])) return s[1].ToString(); // D0..D9
        if (s.StartsWith("NumPad")) return "Num" + s[6..];
        return s;
    }
}
