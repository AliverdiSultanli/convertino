using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Convertino.Editors;

/// <summary>
/// Draws an image fitted into the available space and maps between view and image pixel coordinates,
/// so overlays (crop handles, redaction boxes, ink) stay crisp at any zoom.
/// </summary>
public class ImageSurface : FrameworkElement
{
    public BitmapSource? Image { get; set; }
    /// <summary>Image size in source pixels (the preview bitmap may be smaller).</summary>
    public Size ImageSize { get; set; }
    public double Pad { get; set; } = 18;

    protected double Scale { get; private set; } = 1;
    protected Point Offset { get; private set; }

    public ImageSurface()
    {
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SizeChanged += (_, _) => InvalidateVisual();
    }

    protected Rect ImageRect
    {
        get
        {
            if (ImageSize.Width <= 0) return Rect.Empty;
            double aw = Math.Max(1, ActualWidth - Pad * 2), ah = Math.Max(1, ActualHeight - Pad * 2);
            Scale = Math.Min(aw / ImageSize.Width, ah / ImageSize.Height);
            double w = ImageSize.Width * Scale, h = ImageSize.Height * Scale;
            Offset = new Point((ActualWidth - w) / 2, (ActualHeight - h) / 2);
            return new Rect(Offset, new Size(w, h));
        }
    }

    public Point ToImage(Point view)
    {
        _ = ImageRect;
        return new Point((view.X - Offset.X) / Scale, (view.Y - Offset.Y) / Scale);
    }

    public Point ToView(Point img)
    {
        _ = ImageRect;
        return new Point(Offset.X + img.X * Scale, Offset.Y + img.Y * Scale);
    }

    public Rect ToView(Rect img) => new(ToView(img.TopLeft), ToView(img.BottomRight));

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var r = ImageRect;
        if (Image == null || r.IsEmpty) return;
        // checkerboard under transparent images
        dc.DrawRectangle(Checker, null, r);
        dc.DrawImage(Image, r);
        DrawOverlay(dc, r);
    }

    protected virtual void DrawOverlay(DrawingContext dc, Rect imageRect) { }

    static Brush? _checker;
    static Brush Checker
    {
        get
        {
            if (_checker != null) return _checker;
            var g = new DrawingGroup();
            using (var dc = g.Open())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xee, 0xee, 0xee)), null, new Rect(0, 0, 16, 16));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xd8, 0xd8, 0xd8)), null, new Rect(0, 0, 8, 8));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xd8, 0xd8, 0xd8)), null, new Rect(8, 8, 8, 8));
            }
            var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
            b.Freeze();
            return _checker = b;
        }
    }
}
