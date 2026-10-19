using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Convertino.Overlay;
using Convertino.Ui;

namespace Convertino;

/// <summary>The Convertino mark, drawn in code.</summary>
public static class AppIcon
{
    public static Bitmap Render(int px, Skin? theme = null)
    {
        var t = theme ?? Skin.All[0];
        var rtb = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext(true))
        using (dc.PushTransform(Matrix.CreateScale(px / 100.0, px / 100.0)))
        {
            var c = new Point(50, 50);
            var acc = t.AccentGradient(new Rect(0, 0, 100, 100));
            const int n = 6;
            for (int i = 0; i < n; i++)
            {
                double span = 2 * Math.PI / n, mid = -Math.PI / 2 + i * span;
                var geo = FlowerView.PetalGeometry(c, 21, 49, mid - span / 2, mid + span / 2, 6, 9);
                using (dc.PushOpacity(i % 2 == 0 ? 1 : 0.72)) dc.DrawGeometry(acc, null, geo);
            }
            dc.DrawEllipse(new ImmutableSolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x2e)), new Pen(acc, 3), c, 15, 15);
            dc.DrawEllipse(acc, null, c, 5.5, 5.5);
        }
        return rtb;
    }
}
