using System.Text;
using Convertino.Services;
using ImageMagick;

namespace Convertino.Native;

/// <summary>
/// Text recognition with Apple's Vision framework (VNRecognizeTextRequest, macOS 10.15+): on-device, no install,
/// detects the language by itself on macOS 13+. Synchronous perform — no blocks needed from .NET.
/// </summary>
public static class VisionOcr
{
    static readonly Lazy<bool> Loaded = new(() => ObjC.Load("Foundation") && ObjC.Load("Vision")
                                                   && ObjC.Class("VNRecognizeTextRequest") != IntPtr.Zero);
    static readonly object Gate = new();

    public static bool Available => Loaded.Value;

    public static OcrResult Recognize(byte[] image)
    {
        if (!Available) throw new InvalidOperationException("Vision framework unavailable");
        var info = new MagickImageInfo(image);
        double W = info.Width, H = info.Height;

        lock (Gate)
        {
            var pool = ObjC.objc_autoreleasePoolPush();
            IntPtr handler = IntPtr.Zero, request = IntPtr.Zero;
            try
            {
                handler = ObjC.Send(ObjC.Send(ObjC.Class("VNImageRequestHandler"), "alloc"), "initWithData:options:",
                    ObjC.NSData(image), ObjC.Send(ObjC.Class("NSDictionary"), "dictionary"));
                request = ObjC.New("VNRecognizeTextRequest");
                ObjC.Send(request, "setRecognitionLevel:", (IntPtr)0);    // accurate
                ObjC.Send(request, "setUsesLanguageCorrection:", true);
                ObjC.TrySend(request, "setAutomaticallyDetectsLanguage:", true);
                var requests = ObjC.Send(ObjC.Class("NSArray"), "arrayWithObject:", request);
                if (!ObjC.SendBool(handler, "performRequests:error:", requests, out var error))
                    throw new IOException("Vision: " + (error == IntPtr.Zero ? "recognition failed" : ObjC.Str(ObjC.Send(error, "localizedDescription"))));

                var sb = new StringBuilder();
                var lines = new List<RectD>();
                var words = new List<RectD>();
                var results = ObjC.Send(request, "results");
                for (nuint i = 0, n = ObjC.Count(results); i < n; i++)
                {
                    var obs = ObjC.At(results, i);
                    var candidates = ObjC.Send(obs, "topCandidates:", (IntPtr)1);
                    if (ObjC.Count(candidates) == 0) continue;
                    var best = ObjC.At(candidates, 0);
                    var text = ObjC.Str(ObjC.Send(best, "string")) ?? "";
                    if (text.Trim().Length == 0) continue;
                    sb.AppendLine(text);
                    lines.Add(ToPixels(ObjC.SendRect(obs, "boundingBox"), W, H));
                    // NSString ranges are UTF-16 offsets, the same as .NET string indices
                    for (int s = 0; s < text.Length;)
                    {
                        while (s < text.Length && char.IsWhiteSpace(text[s])) s++;
                        int e = s;
                        while (e < text.Length && !char.IsWhiteSpace(text[e])) e++;
                        if (e > s)
                        {
                            var box = ObjC.Send(best, "boundingBoxForRange:error:", new NSRange(s, e - s), IntPtr.Zero);
                            if (box != IntPtr.Zero) words.Add(ToPixels(ObjC.SendRect(box, "boundingBox"), W, H));
                        }
                        s = e;
                    }
                }
                return new OcrResult(sb.ToString().Trim(), lines, words);
            }
            finally
            {
                ObjC.Release(request);
                ObjC.Release(handler);
                ObjC.objc_autoreleasePoolPop(pool);
            }
        }
    }

    /// <summary>Vision boxes are normalised with a bottom-left origin.</summary>
    static RectD ToPixels(CGRect r, double w, double h) =>
        new(r.X * w, (1 - r.Y - r.Height) * h, r.Width * w, r.Height * h);
}
