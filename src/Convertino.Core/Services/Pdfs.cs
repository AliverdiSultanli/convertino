using System.IO;
using System.Text;
using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Convertino.Services;

public static class Pdfs
{
    /// <summary>Render every page to PNG bytes at the given DPI (platform renderer).</summary>
    public static Task<List<byte[]>> RenderPages(string path, double dpi, CancellationToken ct, Action<double>? progress = null, int maxPages = int.MaxValue)
        => Platform.Current.RenderPdf(Path.GetFullPath(path), dpi, ct, progress, maxPages);

    public static async Task ToImages(Job job, string ext)
    {
        int n = job.Inputs.Count;
        for (int f = 0; f < n; f++)
        {
            int idx = f;
            await SaveImages(job, job.Inputs[f], job.Inputs[f], ext, 200, p => job.Report(idx, n, p * 0.8));
        }
    }

    /// <summary>
    /// Render <paramref name="pdf"/> and save the pages beside <paramref name="src"/>:
    /// one page → "name.png", several → folder "name (PNG)/name - 01.png".
    /// </summary>
    public static async Task SaveImages(Job job, string src, string pdf, string ext, double dpi, Action<double>? progress = null)
    {
        var pages = await RenderPages(pdf, dpi, job.Token, progress);
        var stem = Path.GetFileNameWithoutExtension(src);
        var dir = Path.GetDirectoryName(src)!;
        string outDir = dir;
        if (pages.Count > 1)
        {
            outDir = Paths.UniqueDir(dir, $"{stem} ({ext.ToUpperInvariant()})");
            Directory.CreateDirectory(outDir);
        }
        await Task.Run(() =>
        {
            for (int i = 0; i < pages.Count; i++)
            {
                var dst = pages.Count == 1 ? Paths.Beside(src, ext) : Path.Combine(outDir, $"{stem} - {i + 1:D2}.{ext}");
                if (ext == "png") File.WriteAllBytes(dst, pages[i]);
                else
                {
                    using var img = new MagickImage(pages[i]);
                    Images.PrepareFor(img, ext);
                    img.Write(dst, Images.FormatOf(ext));
                }
                if (pages.Count == 1) job.Outputs.Add(dst);
            }
        }, job.Token);
        if (pages.Count > 1) job.Outputs.Add(outDir);
        job.Summary = pages.Count == 1 ? Images.SizeSummary(job) : $"{pages.Count} pages";
    }

    public static string ExtractText(string path)
    {
        var sb = new StringBuilder();
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        foreach (var page in doc.GetPages())
        {
            var t = ContentOrderTextExtractor.GetText(page);
            sb.AppendLine(t.TrimEnd());
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    public static async Task ToText(Job job)
    {
        int n = job.Inputs.Count;
        bool ocrUsed = false;
        for (int i = 0; i < n; i++)
        {
            job.Report(i, n);
            var src = job.Inputs[i];
            string text = "";
            int pageCount = 1;
            try
            {
                text = await Task.Run(() => ExtractText(src), job.Token);
                using var d = UglyToad.PdfPig.PdfDocument.Open(src);
                pageCount = d.NumberOfPages;
            }
            catch (Exception ex) { Log.Error("pdfpig", ex); }
            if (text.Length < 25 * pageCount)
            {
                // scanned PDF — read it with Windows OCR instead
                var pages = await RenderPages(src, 220, job.Token, p => job.Report(i, n, p * 0.5));
                var sb = new StringBuilder();
                foreach (var png in pages) sb.AppendLine((await Platform.Current.Ocr(png)).Text).AppendLine();
                text = sb.ToString().Trim();
                ocrUsed = true;
            }
            var dst = Paths.Beside(src, "txt");
            await File.WriteAllTextAsync(dst, text, new UTF8Encoding(true), job.Token);
            job.Outputs.Add(dst);
        }
        job.Summary = ocrUsed ? "Scanned pages — read with OCR" : Images.SizeSummary(job);
    }

    public static async Task Ocr_(Job job)
    {
        var all = new StringBuilder();
        int n = job.Inputs.Count;
        for (int i = 0; i < n; i++)
        {
            var src = job.Inputs[i];
            var pages = await RenderPages(src, 220, job.Token, p => job.Report(i, n, p * 0.5));
            for (int p = 0; p < pages.Count; p++)
            {
                job.Report(i, n, 0.5 + 0.5 * p / pages.Count);
                all.AppendLine((await Platform.Current.Ocr(pages[p])).Text).AppendLine();
            }
        }
        Platform.Current.SetClipboardText(all.ToString().Trim());
        job.Title = "Text copied";
        job.Summary = Ocr.Preview(all.ToString());
    }

    public static async Task ImagesToPdf(Job job, bool merge)
    {
        var inputs = job.Inputs;
        if (!merge)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                job.Report(i, inputs.Count);
                var dst = Paths.Beside(inputs[i], "pdf");
                await Task.Run(() => WriteImagesPdf([inputs[i]], dst, job), job.Token);
                job.Outputs.Add(dst);
            }
        }
        else
        {
            var first = inputs[0];
            var dir = Path.GetDirectoryName(first)!;
            var name = inputs.Count == 1 ? Path.GetFileNameWithoutExtension(first) : $"{Path.GetFileNameWithoutExtension(first)} +{inputs.Count - 1}";
            var dst = Paths.Unique(dir, name, "pdf");
            await Task.Run(() => WriteImagesPdf(inputs, dst, job), job.Token);
            job.Outputs.Add(dst);
        }
        job.Summary = Images.SizeSummary(job);
    }

    static void WriteImagesPdf(IReadOnlyList<string> images, string dst, Job job)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < images.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            job.Report(i, images.Count);
            var src = images[i];
            var ext = Kinds.Ext(src);
            byte[] data;
            int w, h;
            if (ext is "jpg" or "jpeg" or "jfif")
            {
                using var probe = new MagickImage();
                probe.Ping(src);
                var o = probe.GetExifProfile()?.GetValue(ExifTag.Orientation)?.Value ?? 1;
                if (o <= 1)
                {
                    data = File.ReadAllBytes(src);
                    w = (int)probe.Width; h = (int)probe.Height;
                }
                else (data, w, h) = Encode(src, "jpg");
            }
            else (data, w, h) = Encode(src, ext is "png" or "gif" or "bmp" or "ico" or "svg" ? "png" : "jpg");

            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(w * 0.75);
            page.Height = XUnit.FromPoint(h * 0.75);
            using var gfx = XGraphics.FromPdfPage(page);
            using var ms = new MemoryStream(data);
            using var xi = XImage.FromStream(ms);
            gfx.DrawImage(xi, 0, 0, page.Width.Point, page.Height.Point);
        }
        doc.Info.Creator = "Convertino";
        doc.Save(dst);
    }

    static (byte[], int, int) Encode(string src, string ext)
    {
        using var img = Images.Load(src);
        Images.PrepareFor(img, ext, ext == "jpg" ? 92 : null);
        return (img.ToByteArray(Images.FormatOf(ext)), (int)img.Width, (int)img.Height);
    }

    public static async Task Merge(Job job)
    {
        var first = job.Inputs[0];
        var dst = Paths.Unique(Path.GetDirectoryName(first)!, Path.GetFileNameWithoutExtension(first) + " (merged)", "pdf");
        await Task.Run(() =>
        {
            using var output = new PdfDocument();
            for (int i = 0; i < job.Inputs.Count; i++)
            {
                job.Token.ThrowIfCancellationRequested();
                job.Report(i, job.Inputs.Count);
                var src = job.Inputs[i];
                if (Kinds.Of(src) == Kind.Image)
                {
                    var tmp = Path.Combine(Path.GetTempPath(), $"petal-{Guid.NewGuid():N}.pdf");
                    WriteImagesPdf([src], tmp, job);
                    using (var imp = PdfReader.Open(tmp, PdfDocumentOpenMode.Import))
                        foreach (var p in imp.Pages) output.AddPage(p);
                    File.Delete(tmp);
                    continue;
                }
                using var input = PdfReader.Open(src, PdfDocumentOpenMode.Import);
                foreach (var p in input.Pages) output.AddPage(p);
            }
            output.Save(dst);
        }, job.Token);
        job.Outputs.Add(dst);
        job.Summary = $"{job.Inputs.Count} files → 1 PDF · {Paths.HumanSize(Paths.SizeOf(dst))}";
    }

    public static async Task Split(Job job)
    {
        foreach (var src in job.Inputs)
        {
            var stem = Path.GetFileNameWithoutExtension(src);
            var outDir = Paths.UniqueDir(Path.GetDirectoryName(src)!, $"{stem} (pages)");
            int count = 0;
            await Task.Run(() =>
            {
                using var input = PdfReader.Open(src, PdfDocumentOpenMode.Import);
                Directory.CreateDirectory(outDir);
                count = input.PageCount;
                for (int i = 0; i < input.PageCount; i++)
                {
                    job.Token.ThrowIfCancellationRequested();
                    job.Report(i, input.PageCount);
                    using var one = new PdfDocument();
                    one.AddPage(input.Pages[i]);
                    one.Save(Path.Combine(outDir, $"{stem} - {i + 1:D2}.pdf"));
                }
            }, job.Token);
            job.Outputs.Add(outDir);
            job.Summary = $"{count} pages";
        }
    }

    public static async Task Rotate(Job job)
    {
        foreach (var src in job.Inputs)
        {
            var dst = Paths.Beside(src, "pdf", "(rotated)");
            await Task.Run(() =>
            {
                using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
                foreach (var p in doc.Pages) p.Rotate = (p.Rotate + 90) % 360;
                doc.Save(dst);
            }, job.Token);
            job.Outputs.Add(dst);
        }
        job.Summary = "Rotated 90° clockwise";
    }

    public static async Task Clean(Job job)
    {
        foreach (var src in job.Inputs)
        {
            var dst = Paths.Beside(src, "pdf", "(clean)");
            await Task.Run(() =>
            {
                using var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify);
                var info = doc.Info;
                info.Title = ""; info.Author = ""; info.Subject = ""; info.Keywords = ""; info.Creator = "";
                foreach (var key in info.Elements.Keys.ToList())
                    if (key is not ("/Producer" or "/CreationDate" or "/ModDate")) info.Elements.Remove(key);
                doc.Internals.Catalog.Elements.Remove("/Metadata");
                doc.Save(dst);
            }, job.Token);
            job.Outputs.Add(dst);
        }
        job.Summary = "Author, title & XMP metadata removed";
    }
}
