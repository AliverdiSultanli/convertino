using ImageMagick;

namespace Convertino.Services;

public enum RedactStyle { Pixelate, Blur, Black }

/// <summary>Adjust editor parameters. Sliders are −100…100 except Sharpness/Vignette (0…100) and Scale (%).</summary>
public sealed record AdjustParams(double Brightness, double Contrast, double Saturation, double Warmth, double Sharpness, double Vignette, int Rotate, bool FlipH, bool FlipV, int Scale)
{
    public static readonly AdjustParams Default = new(0, 0, 0, 0, 0, 0, 0, false, false, 100);
}

/// <summary>The pixel work behind the interactive editors — shared by every UI.</summary>
public static class EditOps
{
    /// <summary>Keep the source format when we can write it, else PNG.</summary>
    public static string OutputExt(string src)
    {
        var e = Kinds.Ext(src);
        return e switch
        {
            "jpg" or "jpeg" or "jfif" => "jpg",
            "png" or "webp" or "bmp" or "gif" => e,
            "tif" or "tiff" => "tiff",
            "avif" when Images.CanWrite(MagickFormat.Avif) => "avif",
            "heic" or "heif" when Images.CanWrite(MagickFormat.Heic) => "heic",
            _ => "png",
        };
    }

    public static MagickImage Downscaled(MagickImage img, uint max)
    {
        var p = (MagickImage)img.Clone();
        if (Math.Max(p.Width, p.Height) > max) p.Resize(new MagickGeometry(max, max));
        return p;
    }

    public static void Write(MagickImage img, string dst, string ext)
    {
        Images.PrepareFor(img, ext, ext == "jpg" ? 94 : null);
        img.Write(dst, Images.FormatOf(ext));
    }

    // ---------- crop ----------

    public static string Crop(string src, MagickImage img, RectD c)
    {
        var ext = OutputExt(src);
        var dst = Paths.Beside(src, ext, "(cropped)");
        using var copy = (MagickImage)img.Clone();
        copy.Crop(new MagickGeometry((int)Math.Round(c.X), (int)Math.Round(c.Y), (uint)Math.Round(c.Width), (uint)Math.Round(c.Height)));
        copy.ResetPage();
        Write(copy, dst, ext);
        return dst;
    }

    // ---------- redact ----------

    public static int BlockFor(MagickImage i) => (int)Math.Max(6, Math.Min(i.Width, i.Height) / 45.0);

    public static MagickImage Pixelate(MagickImage src, int block)
    {
        var p = (MagickImage)src.Clone();
        uint w = Math.Max(1, p.Width / (uint)block), h = Math.Max(1, p.Height / (uint)block);
        p.Scale(w, h);
        p.FilterType = FilterType.Point;
        p.Resize(new MagickGeometry(src.Width, src.Height) { IgnoreAspectRatio = true });
        return p;
    }

    /// <summary>Heavy enough that text can't be recovered.</summary>
    public static MagickImage BlurAll(MagickImage img)
    {
        var b = (MagickImage)img.Clone();
        b.Blur(0, Math.Max(8, Math.Max(img.Width, img.Height) / 80.0));
        return b;
    }

    public static string Redact(string src, MagickImage source, IReadOnlyList<RectD> boxes, RedactStyle style)
    {
        var ext = OutputExt(src);
        var dst = Paths.Beside(src, ext, "(redacted)");
        using var img = (MagickImage)source.Clone();
        img.Strip(); // a redacted image shouldn't leak GPS/camera data either
        using var effect = style switch
        {
            RedactStyle.Pixelate => Pixelate(img, BlockFor(img)),
            RedactStyle.Blur => BlurAll(img),
            _ => null,
        };
        foreach (var b in boxes)
        {
            double x0 = Math.Max(0, b.X), y0 = Math.Max(0, b.Y);
            double x1 = Math.Min(img.Width, b.X + b.Width), y1 = Math.Min(img.Height, b.Y + b.Height);
            if (x1 <= x0 || y1 <= y0) continue;
            var g = new MagickGeometry((int)x0, (int)y0, (uint)Math.Ceiling(x1 - x0), (uint)Math.Ceiling(y1 - y0));
            if (effect == null)
            {
                using var black = new MagickImage(MagickColors.Black, g.Width, g.Height);
                img.Composite(black, g.X, g.Y, CompositeOperator.Over);
            }
            else
            {
                using var part = (MagickImage)effect.Clone();
                part.Crop(g);
                part.ResetPage();
                img.Composite(part, g.X, g.Y, CompositeOperator.Over);
            }
        }
        Write(img, dst, ext);
        return dst;
    }

    // ---------- adjust ----------

    public static void Adjust(MagickImage img, AdjustParams p, bool preview)
    {
        if (p.Rotate != 0) img.Rotate(p.Rotate);
        if (p.FlipH) img.Flop();
        if (p.FlipV) img.Flip();
        if (!preview && p.Scale != 100) img.Resize(new Percentage(p.Scale));
        if (p.Brightness != 0 || p.Contrast != 0)
            img.BrightnessContrast(new Percentage(p.Brightness * 0.6), new Percentage(p.Contrast * 0.6));
        if (p.Saturation != 0) img.Modulate(new Percentage(100), new Percentage(100 + p.Saturation), new Percentage(100));
        if (p.Warmth != 0)
        {
            double k = p.Warmth / 100.0 * 0.12;
            img.Evaluate(Channels.Red, EvaluateOperator.Multiply, 1 + k);
            img.Evaluate(Channels.Blue, EvaluateOperator.Multiply, 1 - k);
        }
        if (p.Sharpness > 0)
        {
            double scale = Math.Max(img.Width, img.Height) / 1400.0;
            img.UnsharpMask(0, Math.Max(0.6, 1.2 * scale), p.Sharpness / 100.0 * 1.6, 0.01);
        }
        if (p.Vignette > 0)
        {
            using var mask = new MagickImage("radial-gradient:white-black", img.Width, img.Height);
            mask.Evaluate(Channels.All, EvaluateOperator.Pow, 2.2);
            mask.Evaluate(Channels.All, EvaluateOperator.Multiply, p.Vignette / 100.0 * 0.85);
            mask.Negate();
            img.Composite(mask, CompositeOperator.Multiply, Channels.RGB);
        }
    }

    public static string SaveAdjusted(string src, MagickImage source, AdjustParams p)
    {
        var ext = OutputExt(src);
        var dst = Paths.Beside(src, ext, "(edited)");
        using var c = (MagickImage)source.Clone();
        Adjust(c, p, preview: false);
        Write(c, dst, ext);
        return dst;
    }

    // ---------- shared by annotate / add-background ----------

    /// <summary>Encode BGRA pixels rendered by a UI toolkit, keeping the source's colour profile.</summary>
    public static string SaveRendered(string src, MagickImage? source, byte[] bgra, int w, int h, string suffix, string? forceExt = null)
    {
        var ext = forceExt ?? OutputExt(src);
        if (ext == "gif") ext = "png";
        var dst = Paths.Beside(src, ext, suffix);
        using var outImg = Images.FromBgra(bgra, w, h);
        var icc = source?.GetColorProfile();
        if (icc != null) outImg.SetProfile(icc);
        Write(outImg, dst, ext);
        return dst;
    }
}
