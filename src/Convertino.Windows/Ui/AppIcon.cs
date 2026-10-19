using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Convertino.Overlay;

namespace Convertino.Ui;

/// <summary>The Petal mark — six glass petals round a dark core — drawn in code at any size.</summary>
public static class AppIcon
{
    public static DrawingGroup Drawing(Theme? theme = null)
    {
        var t = theme ?? Theme.Mocha;
        var g = new DrawingGroup();
        using (var dc = g.Open())
        {
            var c = new Point(50, 50);
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 100, 100));
            var acc = new LinearGradientBrush(t.AccentA, t.AccentB, new Point(0, 0), new Point(1, 1));
            const int n = 6;
            for (int i = 0; i < n; i++)
            {
                double span = 2 * Math.PI / n, mid = -Math.PI / 2 + i * span;
                var geo = FlowerView.PetalGeometry(c, 21, 49, mid - span / 2, mid + span / 2, 6, 9);
                dc.PushOpacity(0.72 + 0.28 * (i % 2 == 0 ? 1 : 0.0));
                dc.DrawGeometry(acc, null, geo);
                dc.Pop();
            }
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x2e)), new Pen(acc, 3), c, 15, 15);
            dc.DrawEllipse(acc, null, c, 5.5, 5.5);
        }
        g.Freeze();
        return g;
    }

    public static BitmapSource Render(int px, Theme? theme = null)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(px / 100.0, px / 100.0));
            dc.DrawDrawing(Drawing(theme));
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    public static byte[] Png(int px)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(Render(px)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Multi-resolution .ico with PNG frames (Vista+ format).</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var frames = sizes.Select(Png).ToList();
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(frames[i].Length); w.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var f in frames) w.Write(f);
    }

    public static System.Drawing.Icon TrayIcon(int px)
    {
        var src = Render(px);
        var pixels = new byte[px * px * 4];
        src.CopyPixels(pixels, px * 4, 0);
        // un-premultiply for GDI+
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte a = pixels[i + 3];
            if (a is > 0 and < 255)
            {
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }
        }
        using var bmp = new System.Drawing.Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, px, px), System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bmp.UnlockBits(data);
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }
}
