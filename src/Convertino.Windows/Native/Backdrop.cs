using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Convertino.Native.Win32;

namespace Convertino.Native;

/// <summary>
/// "Frosted glass" without DWM: grab the screen under where a window is about to appear,
/// shrink it, blur it and boost saturation. Drawn under the glass tint it reads as real vibrancy.
/// </summary>
public static class Backdrop
{
    public static BitmapSource? Capture(RECT r, int downscale = 4, int radius = 6, double saturation = 1.35, double lift = 0)
    {
        int w = Math.Max(1, r.Width / downscale), h = Math.Max(1, r.Height / downscale);
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        var bih = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr dib = CreateDIBSection(screen, ref bih, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
            return null;
        }
        IntPtr old = SelectObject(mem, dib);
        try
        {
            SetStretchBltMode(mem, HALFTONE);
            SetBrushOrgEx(mem, 0, 0, IntPtr.Zero);
            StretchBlt(mem, 0, 0, w, h, screen, r.Left, r.Top, r.Width, r.Height, SRCCOPY | CAPTUREBLT);
            var px = new byte[w * h * 4];
            Marshal.Copy(bits, px, 0, px.Length);
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(px, w, h, radius);
                BoxV(px, w, h, radius);
            }
            Vibrance(px, saturation, lift);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Log.Error("backdrop", ex);
            return null;
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(dib);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    static void BoxH(byte[] p, int w, int h, int r)
    {
        var row = new int[w * 3];
        int win = r * 2 + 1;
        for (int y = 0; y < h; y++)
        {
            int o = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                row[x * 3] = p[o + x * 4];
                row[x * 3 + 1] = p[o + x * 4 + 1];
                row[x * 3 + 2] = p[o + x * 4 + 2];
            }
            int sb = 0, sg = 0, sr = 0;
            for (int i = -r; i <= r; i++)
            {
                int xi = Math.Clamp(i, 0, w - 1) * 3;
                sb += row[xi]; sg += row[xi + 1]; sr += row[xi + 2];
            }
            for (int x = 0; x < w; x++)
            {
                p[o + x * 4] = (byte)(sb / win);
                p[o + x * 4 + 1] = (byte)(sg / win);
                p[o + x * 4 + 2] = (byte)(sr / win);
                p[o + x * 4 + 3] = 255;
                int add = Math.Min(x + r + 1, w - 1) * 3, sub = Math.Max(x - r, 0) * 3;
                sb += row[add] - row[sub]; sg += row[add + 1] - row[sub + 1]; sr += row[add + 2] - row[sub + 2];
            }
        }
    }

    static void BoxV(byte[] p, int w, int h, int r)
    {
        var col = new int[h * 3];
        int win = r * 2 + 1;
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                int o = (y * w + x) * 4;
                col[y * 3] = p[o]; col[y * 3 + 1] = p[o + 1]; col[y * 3 + 2] = p[o + 2];
            }
            int sb = 0, sg = 0, sr = 0;
            for (int i = -r; i <= r; i++)
            {
                int yi = Math.Clamp(i, 0, h - 1) * 3;
                sb += col[yi]; sg += col[yi + 1]; sr += col[yi + 2];
            }
            for (int y = 0; y < h; y++)
            {
                int o = (y * w + x) * 4;
                p[o] = (byte)(sb / win); p[o + 1] = (byte)(sg / win); p[o + 2] = (byte)(sr / win);
                int add = Math.Min(y + r + 1, h - 1) * 3, sub = Math.Max(y - r, 0) * 3;
                sb += col[add] - col[sub]; sg += col[add + 1] - col[sub + 1]; sr += col[add + 2] - col[sub + 2];
            }
        }
    }

    static void Vibrance(byte[] p, double sat, double lift)
    {
        for (int i = 0; i < p.Length; i += 4)
        {
            double b = p[i], g = p[i + 1], r = p[i + 2];
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            r = l + (r - l) * sat + lift; g = l + (g - l) * sat + lift; b = l + (b - l) * sat + lift;
            p[i] = (byte)Math.Clamp(b, 0, 255);
            p[i + 1] = (byte)Math.Clamp(g, 0, 255);
            p[i + 2] = (byte)Math.Clamp(r, 0, 255);
        }
    }
}
