using System.Runtime.InteropServices;

namespace Convertino.Native;

[StructLayout(LayoutKind.Sequential)]
public struct CGPoint { public double X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct CGSize { public double Width, Height; public CGSize(double w, double h) { Width = w; Height = h; } }

[StructLayout(LayoutKind.Sequential)]
public struct CGRect
{
    public double X, Y, Width, Height;
    public CGRect(double x, double y, double w, double h) { X = x; Y = y; Width = w; Height = h; }
}

[StructLayout(LayoutKind.Sequential)]
public struct NSRange { public nuint Location, Length; public NSRange(int loc, int len) { Location = (nuint)loc; Length = (nuint)len; } }

/// <summary>
/// The few Objective-C messages Convertino sends to AppKit, Foundation and Vision itself (Avalonia covers the rest).
/// Every selector is checked with respondsToSelector: first: an unknown one would raise an Objective-C
/// exception that .NET can't catch.
/// </summary>
public static class ObjC
{
    const string Lib = "/usr/lib/libobjc.A.dylib";

    [DllImport(Lib)] static extern IntPtr objc_getClass(string name);
    [DllImport(Lib)] static extern IntPtr sel_registerName(string name);
    [DllImport(Lib)] public static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(Lib)] public static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a, IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a, nint b, IntPtr c);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, double a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, CGSize a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, CGRect a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, NSRange a, IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, byte[] a, nuint b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr o, IntPtr s, IntPtr a, out IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool MsgBool(IntPtr o, IntPtr s, IntPtr a);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool MsgBool(IntPtr o, IntPtr s, IntPtr a, out IntPtr b);
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern nuint MsgUInt(IntPtr o, IntPtr s);
    // a CGRect comes back in d0–d3 on arm64, through a hidden pointer (objc_msgSend_stret) on x86-64
    [DllImport(Lib, EntryPoint = "objc_msgSend")] static extern CGRect MsgRect(IntPtr o, IntPtr s);
    [DllImport(Lib, EntryPoint = "objc_msgSend_stret")] static extern void MsgRectStret(out CGRect r, IntPtr o, IntPtr s);

    public static IntPtr Class(string name) => objc_getClass(name);
    public static IntPtr Sel(string name) => sel_registerName(name);

    public static bool Responds(IntPtr o, string sel) => o != IntPtr.Zero && MsgBool(o, Sel("respondsToSelector:"), Sel(sel));

    static IntPtr S(IntPtr o, string sel) => Responds(o, sel) ? Sel(sel) : throw new MissingMethodException("Objective-C", sel);

    public static IntPtr Send(IntPtr o, string sel) => Msg(o, S(o, sel));
    public static IntPtr Send(IntPtr o, string sel, IntPtr a) => Msg(o, S(o, sel), a);
    public static IntPtr Send(IntPtr o, string sel, IntPtr a, IntPtr b) => Msg(o, S(o, sel), a, b);
    public static IntPtr Send(IntPtr o, string sel, IntPtr a, nint b, IntPtr c) => Msg(o, S(o, sel), a, b, c);
    public static IntPtr Send(IntPtr o, string sel, double a) => Msg(o, S(o, sel), a);
    public static IntPtr Send(IntPtr o, string sel, bool a) => Msg(o, S(o, sel), a);
    public static IntPtr Send(IntPtr o, string sel, CGSize a) => Msg(o, S(o, sel), a);
    public static IntPtr Send(IntPtr o, string sel, CGRect a) => Msg(o, S(o, sel), a);
    public static IntPtr Send(IntPtr o, string sel, NSRange a, IntPtr b) => Msg(o, S(o, sel), a, b);
    public static IntPtr Send(IntPtr o, string sel, byte[] a, nuint b) => Msg(o, S(o, sel), a, b);
    public static IntPtr Send(IntPtr o, string sel, IntPtr a, out IntPtr b) => Msg(o, S(o, sel), a, out b);
    public static bool SendBool(IntPtr o, string sel, IntPtr a) => MsgBool(o, S(o, sel), a);
    public static bool SendBool(IntPtr o, string sel, IntPtr a, out IntPtr b) => MsgBool(o, S(o, sel), a, out b);
    public static nuint SendUInt(IntPtr o, string sel) => MsgUInt(o, S(o, sel));

    public static CGRect SendRect(IntPtr o, string sel)
    {
        var s = S(o, sel);
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return MsgRect(o, s);
        MsgRectStret(out var r, o, s);
        return r;
    }

    /// <summary>Messages a method only if the object has it (newer-OS niceties).</summary>
    public static void TrySend(IntPtr o, string sel, bool a) { if (Responds(o, sel)) Msg(o, Sel(sel), a); }

    public static IntPtr New(string cls) => Send(Send(Class(cls), "alloc"), "init");
    public static void Release(IntPtr o) { if (o != IntPtr.Zero) Msg(o, Sel("release")); }

    public static IntPtr NSString(string s)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(s);
        try { return Send(Class("NSString"), "stringWithUTF8String:", utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    public static string? Str(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, "UTF8String"));

    public static IntPtr NSData(byte[] bytes) => Send(Class("NSData"), "dataWithBytes:length:", bytes, (nuint)bytes.Length);

    public static nuint Count(IntPtr nsArray) => nsArray == IntPtr.Zero ? 0 : SendUInt(nsArray, "count");
    public static IntPtr At(IntPtr nsArray, nuint i) => Send(nsArray, "objectAtIndex:", (nint)i);

    /// <summary>Make sure a framework's classes are registered (headless CLI runs never load AppKit).</summary>
    public static bool Load(string framework)
    {
        try { NativeLibrary.Load($"/System/Library/Frameworks/{framework}.framework/{framework}"); return true; }
        catch (Exception ex) { Log.Error("load " + framework, ex); return false; }
    }
}

/// <summary>AppKit window tweaks Avalonia doesn't expose.</summary>
public static class MacWindow
{
    public const nint StatusLevel = 25, PopUpMenuLevel = 101;

    // NSWindowCollectionBehavior: canJoinAllSpaces | stationary | ignoresCycle | fullScreenAuxiliary
    const nint FloatingBehavior = 1 << 0 | 1 << 4 | 1 << 6 | 1 << 8;

    public static IntPtr NSWindow(Avalonia.Controls.TopLevel top) =>
        top.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle h ? h.NSWindow : IntPtr.Zero;

    public static IntPtr NSView(Avalonia.Controls.TopLevel top) =>
        top.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle h ? h.NSView : IntPtr.Zero;

    /// <summary>
    /// Above every app (and the Dock), on every Space including full-screen ones, out of Cmd-` cycling,
    /// no system shadow (the flower and the cards draw their own). Re-apply after each Show.
    /// </summary>
    public static void Float(Avalonia.Controls.TopLevel top, nint level)
    {
        var w = NSWindow(top);
        if (w == IntPtr.Zero) return;
        try
        {
            ObjC.Send(w, "setLevel:", level);
            ObjC.Send(w, "setCollectionBehavior:", FloatingBehavior);
            ObjC.Send(w, "setHidesOnDeactivate:", false);
            ObjC.Send(w, "setHasShadow:", false);
        }
        catch (Exception ex) { Log.Error("float window", ex); }
    }

    /// <summary>Native state for the debug log: window number (for screencapture -l), visibility, level, frame.</summary>
    public static string Describe(Avalonia.Controls.TopLevel top)
    {
        var w = NSWindow(top);
        if (w == IntPtr.Zero) return "no NSWindow";
        try
        {
            var f = ObjC.SendRect(w, "frame");
            return $"window={(long)ObjC.Send(w, "windowNumber")} visible={((long)ObjC.Send(w, "isVisible") & 0xff) != 0} " +
                   $"level={(long)ObjC.Send(w, "level")} occlusion={(long)ObjC.Send(w, "occlusionState")} frame={f.X},{f.Y} {f.Width}x{f.Height}";
        }
        catch (Exception ex) { return "describe failed: " + ex.Message; }
    }

    /// <summary>Hand focus back to whichever app had it (after a click-mode flower closes).</summary>
    public static void Deactivate()
    {
        try
        {
            var app = ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication");
            if (ObjC.Responds(app, "deactivate")) ObjC.Send(app, "deactivate");
        }
        catch (Exception ex) { Log.Error("deactivate", ex); }
    }
}

/// <summary>
/// Native frosted glass under the flower: an NSVisualEffectView behind Avalonia's content view, masked to the
/// petal silhouette. Behind-window blur is done by the window server, so no Screen Recording permission is needed.
/// </summary>
public sealed class Vibrancy
{
    readonly IntPtr _view;
    readonly Avalonia.Threading.DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(16) };
    double _alpha, _from, _to;
    DateTime _start;
    TimeSpan _duration;

    Vibrancy(IntPtr view)
    {
        _view = view;
        _fade.Tick += (_, _) =>
        {
            double t = _duration.TotalMilliseconds <= 0 ? 1 : Math.Clamp((DateTime.UtcNow - _start) / _duration, 0, 1);
            SetAlpha(_from + (_to - _from) * (1 - (1 - t) * (1 - t)));
            if (t >= 1) _fade.Stop();
        };
    }

    public static Vibrancy? Attach(Avalonia.Controls.TopLevel top, double width, double height)
    {
        if (Environment.GetEnvironmentVariable("CONVERTINO_NOVIBRANCY") == "1") return null;
        try
        {
            var content = MacWindow.NSView(top);
            if (content == IntPtr.Zero) return null;
            var container = ObjC.Send(content, "superview");
            if (container == IntPtr.Zero) return null;
            var fx = ObjC.New("NSVisualEffectView");
            ObjC.Send(fx, "setFrame:", new CGRect(0, 0, width, height));
            ObjC.Send(fx, "setAutoresizingMask:", (nint)(2 | 16));   // width + height sizable
            ObjC.Send(fx, "setMaterial:", Material());
            ObjC.Send(fx, "setBlendingMode:", (nint)0);               // behind window
            ObjC.Send(fx, "setState:", (nint)1);                      // always active: the overlay never has focus
            ObjC.Send(fx, "setAlphaValue:", 0.0);
            ObjC.Send(container, "addSubview:positioned:relativeTo:", fx, -1, content);   // NSWindowBelow
            ObjC.Release(fx);                                          // the superview keeps it
            return new Vibrancy(fx);
        }
        catch (Exception ex) { Log.Error("vibrancy", ex); return null; }
    }

    /// <summary>NSVisualEffectMaterial; CONVERTINO_GLASS_MATERIAL overrides it (to compare looks).</summary>
    static nint Material() =>
        int.TryParse(Environment.GetEnvironmentVariable("CONVERTINO_GLASS_MATERIAL"), out var m) ? m : 13;   // HUD window

    /// <summary>Only the opaque pixels of <paramref name="png"/> (sized <paramref name="width"/>×<paramref name="height"/> points) get the blur.</summary>
    public void SetMask(byte[] png, double width, double height)
    {
        try
        {
            var img = ObjC.Send(ObjC.Send(ObjC.Class("NSImage"), "alloc"), "initWithData:", ObjC.NSData(png));
            if (img == IntPtr.Zero) return;
            ObjC.Send(img, "setSize:", new CGSize(width, height));
            ObjC.Send(_view, "setMaskImage:", img);
            ObjC.Release(img);
        }
        catch (Exception ex) { Log.Error("vibrancy mask", ex); }
    }

    public void FadeTo(double alpha, int ms)
    {
        _from = _alpha;
        _to = alpha;
        _start = DateTime.UtcNow;
        _duration = TimeSpan.FromMilliseconds(ms);
        if (ms <= 0) { _fade.Stop(); SetAlpha(alpha); return; }
        _fade.Start();
    }

    void SetAlpha(double a)
    {
        _alpha = a;
        try { ObjC.Send(_view, "setAlphaValue:", a); }
        catch (Exception ex) { Log.Error("vibrancy alpha", ex); _fade.Stop(); }
    }
}
