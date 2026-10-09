using System.Runtime.InteropServices;
using System.Text;

namespace StreamOrchestrator.Player;

/// <summary>
/// Thin P/Invoke layer over libmpv (client API). All strings are marshalled as UTF-8, as libmpv
/// expects. Negative return codes are libmpv errors; <see cref="Check"/> turns them into exceptions.
/// </summary>
internal static class MpvInterop
{
    private const string Dll = "libmpv-2.dll";

    // mpv_format values from client.h
    public const int MPV_FORMAT_INT64 = 4;

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr ctx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr ctx);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option(IntPtr ctx, byte[] name, int format, ref long data);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] data);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_set_property_string(IntPtr ctx, byte[] name, byte[] data);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern int mpv_command(IntPtr ctx, IntPtr args);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr mpv_error_string(int error);

    // ---- Managed helpers -------------------------------------------------

    private static byte[] Utf8Z(string s) => Encoding.UTF8.GetBytes(s + "\0");

    public static void SetOptionString(IntPtr ctx, string name, string value) =>
        Check(mpv_set_option_string(ctx, Utf8Z(name), Utf8Z(value)), $"set_option {name}={value}");

    public static void SetOptionInt64(IntPtr ctx, string name, long value)
    {
        long data = value;
        Check(mpv_set_option(ctx, Utf8Z(name), MPV_FORMAT_INT64, ref data), $"set_option {name}={value}");
    }

    public static void SetPropertyString(IntPtr ctx, string name, string value) =>
        Check(mpv_set_property_string(ctx, Utf8Z(name), Utf8Z(value)), $"set_property {name}={value}");

    /// <summary>Runs an mpv command given as a NULL-terminated argument vector.</summary>
    public static void Command(IntPtr ctx, params string[] args)
    {
        var ptrs = new IntPtr[args.Length + 1];
        var arr = Marshal.AllocHGlobal(IntPtr.Size * ptrs.Length);
        try
        {
            for (int i = 0; i < args.Length; i++) ptrs[i] = MarshalUtf8(args[i]);
            ptrs[args.Length] = IntPtr.Zero;
            Marshal.Copy(ptrs, 0, arr, ptrs.Length);
            Check(mpv_command(ctx, arr), "command " + string.Join(' ', args));
        }
        finally
        {
            Marshal.FreeHGlobal(arr);
            foreach (var p in ptrs)
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        }
    }

    private static IntPtr MarshalUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var p = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        Marshal.WriteByte(p, bytes.Length, 0);
        return p;
    }

    private static void Check(int code, string what)
    {
        if (code >= 0) return;
        var msg = Marshal.PtrToStringAnsi(mpv_error_string(code)) ?? code.ToString();
        throw new MpvException($"libmpv error during '{what}': {msg} ({code})");
    }
}

public sealed class MpvException : Exception
{
    public MpvException(string message) : base(message) { }
}
