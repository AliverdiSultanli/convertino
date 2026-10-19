using System.Windows;
using System.Windows.Media;

namespace Convertino.Ui;

public sealed record Theme(
    string Name,
    bool IsDark,
    Color Tint,          // laid over the blurred backdrop
    Color PetalTop,
    Color PetalBottom,
    Color Stroke,
    Color Text,
    Color SubText,
    Color AccentA,
    Color AccentB,
    Color AccentText,
    Color Hub,
    Color Shadow,
    Color Window,        // solid surface for editors / fallbacks
    Color Surface,
    Color Danger)
{
    static Color U(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>WPF colours for a shared core palette.</summary>
    public static Theme From(Palette p) => new(p.Name, p.IsDark,
        U(p.Tint), U(p.PetalTop), U(p.PetalBottom), U(p.Stroke), U(p.Text), U(p.SubText),
        U(p.AccentA), U(p.AccentB), U(p.AccentText), U(p.Hub), U(p.Shadow), U(p.Window), U(p.Surface), U(p.Danger));

    public static readonly Theme[] All = Palette.All.Select(From).ToArray();
    public static readonly Theme Mocha = All[0];

    public static Theme Current { get; private set; } = Mocha;

    /// <summary>Denser glass for text-heavy surfaces (toasts, panels) so they read on any wallpaper.</summary>
    public static Color Solid(Color tint)
    {
        tint.A = (byte)Math.Max(tint.A, Current.IsDark ? 205 : 185);
        return tint;
    }

    public static event Action? Changed;

    public static void Apply(string name)
    {
        Current = All.FirstOrDefault(t => t.Name == name) ?? Mocha;
        var r = Application.Current.Resources;
        Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        Color A(Color c, double a) { c.A = (byte)(a * 255); return c; }
        var t = Current;
        r["Convertino.Text"] = B(t.Text);
        r["Convertino.SubText"] = B(t.SubText);
        r["Convertino.Accent"] = B(t.AccentA);
        r["Convertino.AccentText"] = B(t.AccentText);
        r["Convertino.Danger"] = B(t.Danger);
        r["Convertino.Window"] = B(t.Window);
        r["Convertino.WindowTranslucent"] = B(A(t.Window, t.IsDark ? .55 : .5));
        r["Convertino.Surface"] = B(A(t.Surface, t.IsDark ? .55 : .7));
        r["Convertino.SurfaceHover"] = B(A(t.Surface, t.IsDark ? .85 : 1));
        r["Convertino.SurfaceSolid"] = B(t.Surface);
        r["Convertino.Border"] = B(t.IsDark ? A(Colors.White, .09) : A(Colors.Black, .08));
        r["Convertino.Canvas"] = B(t.IsDark ? A(Colors.Black, .28) : A(Colors.Black, .06));
        var g = new LinearGradientBrush(t.AccentA, t.AccentB, new Point(0, 0), new Point(1, 1));
        g.Freeze();
        r["Convertino.AccentGradient"] = g;
        Changed?.Invoke();
    }
}
