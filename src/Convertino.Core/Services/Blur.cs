namespace Convertino.Services;

/// <summary>
/// CPU frosted-glass helpers on 8-bit BGRA buffers: downscale, 3-pass box blur (≈ Gaussian), vibrance.
/// Used for the flower's backdrop on every platform.
/// </summary>
public static class Blur
{
    /// <summary>Average k×k blocks (fast, alias-free downscale).</summary>
    public static byte[] Downscale(byte[] src, int w, int h, int stride, int k, out int ow, out int oh)
    {
        ow = Math.Max(1, w / k);
        oh = Math.Max(1, h / k);
        var dst = new byte[ow * oh * 4];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                int b = 0, g = 0, r = 0, n = 0;
                for (int dy = 0; dy < k; dy++)
                {
                    int sy = y * k + dy;
                    if (sy >= h) break;
                    int row = sy * stride;
                    for (int dx = 0; dx < k; dx++)
                    {
                        int sx = x * k + dx;
                        if (sx >= w) break;
                        int o = row + sx * 4;
                        b += src[o]; g += src[o + 1]; r += src[o + 2]; n++;
                    }
                }
                int d = (y * ow + x) * 4;
                n = Math.Max(1, n);
                dst[d] = (byte)(b / n); dst[d + 1] = (byte)(g / n); dst[d + 2] = (byte)(r / n); dst[d + 3] = 255;
            }
        return dst;
    }

    public static void Frost(byte[] px, int w, int h, int radius, double saturation, double lift = 0)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            BoxH(px, w, h, radius);
            BoxV(px, w, h, radius);
        }
        Vibrance(px, saturation, lift);
    }

    /// <summary>True when the capture is (almost) entirely black — e.g. XWayland can't see Wayland windows.</summary>
    public static bool LooksEmpty(byte[] px)
    {
        long sum = 0;
        for (int i = 0; i < px.Length; i += 16) sum += px[i] + px[i + 1] + px[i + 2];
        return sum / Math.Max(1, px.Length / 16) < 6;
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
