using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Convertino.Services;

/// <summary>
/// Uses the installed Microsoft Office through COM automation: the only way to get
/// pixel-perfect DOCX/XLSX/PPTX ⇄ PDF on Windows without shipping LibreOffice.
/// Every call runs on its own STA thread; we only ever quit Office instances we started.
/// </summary>
public static class MsOffice
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool HasWord => Type.GetTypeFromProgID("Word.Application") != null;
    public static bool HasExcel => Type.GetTypeFromProgID("Excel.Application") != null;
    public static bool HasPowerPoint => Type.GetTypeFromProgID("PowerPoint.Application") != null;

    // Word SaveAs2 formats
    public const int WdDocx = 16, WdPdf = 17, WdRtf = 6, WdOdt = 23, WdHtml = 10, WdTxt = 7, WdDoc = 0;

    static Task Sta(Action body, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        var t = new Thread(() =>
        {
            try { body(); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex is COMException ce ? new IOException(Friendly(ce), ce) : ex); }
        }) { IsBackground = true, Name = "Convertino Office" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task.WaitAsync(TimeSpan.FromMinutes(4), ct);
    }

    static string Friendly(COMException ex) => ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
        ? "The document is password-protected"
        : $"Office couldn't convert this file ({ex.Message.Trim()})";

    static HashSet<int> Pids(string name) => Process.GetProcessesByName(name).Select(p => p.Id).ToHashSet();

    /// <summary>
    /// Work on a private copy: never touches a document the user has open in Word, and a forced kill
    /// can't leave Word's "this file caused a serious error" flag on the real file name.
    /// </summary>
    public static string TempCopy(string src)
    {
        var dir = Path.Combine(Paths.Temp, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var dst = Path.Combine(dir, Path.GetFileName(src));
        File.Copy(src, dst);
        return dst;
    }

    public static void DropTemp(string copy)
    {
        try { Directory.Delete(Path.GetDirectoryName(copy)!, true); } catch { }
    }

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);

    /// <summary>
    /// Some Office builds insist on a one-time prompt (e.g. "Word will now convert your PDF") even when
    /// automated. If Office goes quiet for a while, show its window so the user can answer it once.
    /// </summary>
    static IDisposable Watchdog(string processName, HashSet<int> before, string what)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(9), cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            foreach (var p in Process.GetProcessesByName(processName).Where(p => !before.Contains(p.Id)))
            {
                EnumWindows((h, _) =>
                {
                    Native.Win32.GetWindowThreadProcessId(h, out var pid);
                    if (pid == p.Id && Native.Win32.ClassOf(h) is "OpusApp" or "XLMAIN" or "PPTFrameClass")
                    {
                        ShowWindow(h, 1);
                        Native.Win32.ForceForeground(h);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            Log.Info($"{what}: Office looked stuck on a prompt - window shown to the user");
        }, TaskScheduler.Default);
        return cts;
    }

    /// <summary>Run work inside a Word instance; kills only a WINWORD we spawned if it hangs.</summary>
    public static async Task Word(Job job, Action<dynamic, string, int> perFile)
    {
        await Gate.WaitAsync(job.Token);
        try
        {
            // PDF reflow would otherwise pop an invisible "Word will now convert…" prompt and hang
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Office\16.0\Word\Options");
                k.SetValue("DisableConvertPdfWarning", 1, RegistryValueKind.DWord);
            }
            catch { }
            var before = Pids("WINWORD");
            using var dog = Watchdog("WINWORD", before, "Word");
            try
            {
                await Sta(() =>
                {
                    dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
                    try
                    {
                        app.DisplayAlerts = 0;
                        try { app.Options.ConfirmConversions = false; } catch { }
                        for (int i = 0; i < job.Inputs.Count; i++)
                        {
                            job.Token.ThrowIfCancellationRequested();
                            job.Report(i, job.Inputs.Count, 0.1);
                            perFile(app, job.Inputs[i], i);
                        }
                    }
                    finally
                    {
                        try
                        {
                            bool mine = !(bool)app.Visible && (int)app.Documents.Count == 0;
                            if (mine) app.Quit(0);
                        }
                        catch { }
                        Marshal.FinalReleaseComObject(app);
                    }
                }, job.Token);
            }
            catch
            {
                foreach (var p in Process.GetProcessesByName("WINWORD").Where(p => !before.Contains(p.Id)))
                    try { p.Kill(); } catch { }
                throw;
            }
        }
        finally { Gate.Release(); }
    }

    public static dynamic OpenDoc(dynamic app, string path, int? encoding = null)
    {
        if (encoding.HasValue)
            return app.Documents.Open(FileName: path, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false, Encoding: encoding.Value);
        return app.Documents.Open(FileName: path, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
    }

    public static Task WordConvert(Job job, string ext, int format, Func<string, string>? prepare = null) => Word(job, (app, src, i) =>
    {
        string? prepared = prepare?.Invoke(src);
        string input = prepared ?? TempCopy(src);
        bool isText = Kinds.Of(src) is Kind.Text;
        dynamic doc = OpenDoc(app, input, isText ? 65001 : (int?)null);
        try
        {
            var dst = Paths.Beside(src, ext);
            if (format == WdTxt) doc.SaveAs2(FileName: dst, FileFormat: format, Encoding: 65001, AddToRecentFiles: false);
            else doc.SaveAs2(FileName: dst, FileFormat: format, AddToRecentFiles: false);
            lock (job.Outputs) job.Outputs.Add(dst);
        }
        finally
        {
            doc.Close(SaveChanges: 0);
            Marshal.FinalReleaseComObject(doc);
            if (prepared != null) try { File.Delete(prepared); } catch { }
            else DropTemp(input);
        }
    });

    public static async Task Excel(Job job, string ext)
    {
        await Gate.WaitAsync(job.Token);
        var before = Pids("EXCEL");
        using var dog = Watchdog("EXCEL", before, "Excel");
        try
        {
            await Sta(() =>
            {
                dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application")!)!;
                try
                {
                    app.DisplayAlerts = false;
                    app.ScreenUpdating = false;
                    for (int i = 0; i < job.Inputs.Count; i++)
                    {
                        job.Report(i, job.Inputs.Count, 0.1);
                        var src = job.Inputs[i];
                        var copy = TempCopy(src);
                        dynamic wb = app.Workbooks.Open(copy, 0, true);
                        try
                        {
                            var dst = Paths.Beside(src, ext);
                            switch (ext)
                            {
                                case "pdf": wb.ExportAsFixedFormat(0, dst); break;
                                case "csv": wb.SaveAs(dst, 62); break;   // xlCSVUTF8
                                case "xlsx": wb.SaveAs(dst, 51); break;
                                case "ods": wb.SaveAs(dst, 60); break;
                                case "html": wb.SaveAs(dst, 44); break;
                            }
                            job.Outputs.Add(dst);
                        }
                        finally
                        {
                            wb.Close(false);
                            Marshal.FinalReleaseComObject(wb);
                            DropTemp(copy);
                        }
                    }
                }
                finally
                {
                    try { if (!(bool)app.Visible && (int)app.Workbooks.Count == 0) app.Quit(); } catch { }
                    Marshal.FinalReleaseComObject(app);
                }
            }, job.Token);
        }
        catch
        {
            foreach (var p in Process.GetProcessesByName("EXCEL").Where(p => !before.Contains(p.Id))) try { p.Kill(); } catch { }
            throw;
        }
        finally { Gate.Release(); }
    }

    public static async Task PowerPoint(Job job, string ext)
    {
        await Gate.WaitAsync(job.Token);
        var before = Pids("POWERPNT");
        using var dog = Watchdog("POWERPNT", before, "PowerPoint");
        try
        {
            await Sta(() =>
            {
                dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                try
                {
                    for (int i = 0; i < job.Inputs.Count; i++)
                    {
                        job.Report(i, job.Inputs.Count, 0.1);
                        var src = job.Inputs[i];
                        // ReadOnly=msoTrue, Untitled=msoFalse, WithWindow=msoFalse
                        var copy = TempCopy(src);
                        dynamic pres = app.Presentations.Open(copy, -1, 0, 0);
                        try
                        {
                            if (ext == "png")
                            {
                                var dir = Paths.UniqueDir(Path.GetDirectoryName(src)!, Path.GetFileNameWithoutExtension(src) + " (slides)");
                                Directory.CreateDirectory(dir);
                                float w = (float)pres.PageSetup.SlideWidth, h = (float)pres.PageSetup.SlideHeight;
                                int outW = 1920, outH = (int)Math.Round(1920 * h / w);
                                pres.Export(dir, "PNG", outW, outH);
                                job.Outputs.Add(dir);
                            }
                            else
                            {
                                var dst = Paths.Beside(src, ext);
                                int fmt = ext switch { "pdf" => 32, "pptx" => 24, "odp" => 35, _ => 32 };
                                pres.SaveAs(dst, fmt);
                                job.Outputs.Add(dst);
                            }
                        }
                        finally
                        {
                            pres.Close();
                            Marshal.FinalReleaseComObject(pres);
                            DropTemp(copy);
                        }
                    }
                }
                finally
                {
                    try { if ((int)app.Presentations.Count == 0 && (int)app.Visible == 0) app.Quit(); } catch { }
                    Marshal.FinalReleaseComObject(app);
                }
            }, job.Token);
        }
        catch
        {
            foreach (var p in Process.GetProcessesByName("POWERPNT").Where(p => !before.Contains(p.Id))) try { p.Kill(); } catch { }
            throw;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Word document → page images, via a temporary PDF.</summary>
    public static async Task WordToImages(Job job, string ext)
    {
        var temps = new List<string>();
        var pdfJob = new Job { Inputs = job.Inputs, Action = job.Action, Token = job.Token, OnProgress = p => job.Report(p * 0.5) };
        await Word(pdfJob, (app, src, i) =>
        {
            var copy = TempCopy(src);
            dynamic doc = OpenDoc(app, copy);
            try
            {
                var tmp = Path.Combine(Paths.Temp, $"{Guid.NewGuid():N}.pdf");
                doc.SaveAs2(FileName: tmp, FileFormat: WdPdf, AddToRecentFiles: false);
                lock (temps) temps.Add(tmp);
            }
            finally { doc.Close(SaveChanges: 0); Marshal.FinalReleaseComObject(doc); DropTemp(copy); }
        });
        try
        {
            for (int i = 0; i < temps.Count; i++)
            {
                int idx = i;
                await Pdfs.SaveImages(job, job.Inputs[i], temps[i], ext, 170, p => job.Report(0.5 + 0.5 * (idx + p) / temps.Count));
            }
        }
        finally { foreach (var t in temps) try { File.Delete(t); } catch { } }
    }
}

/// <summary>Microsoft Office as Convertino's document engine.</summary>
public sealed class MsOfficeConverter : IOffice
{
    readonly bool _word = MsOffice.HasWord, _excel = MsOffice.HasExcel, _ppt = MsOffice.HasPowerPoint;

    public static MsOfficeConverter? Find()
    {
        var c = new MsOfficeConverter();
        return c._word || c._excel || c._ppt ? c : null;
    }

    public string Name => "Microsoft Office";

    public bool Supports(Kind kind) => kind switch
    {
        Kind.Word or Kind.Text or Kind.Markdown or Kind.Pdf => _word,
        Kind.Excel => _excel,
        Kind.Slides => _ppt,
        _ => false,
    };

    public Task Convert(Job job, string ext, Func<string, string>? prepare = null)
    {
        var kind = Kinds.Of(job.Inputs[0]);
        if (kind == Kind.Excel) return MsOffice.Excel(job, ext);
        if (kind == Kind.Slides) return MsOffice.PowerPoint(job, ext);
        int fmt = ext switch
        {
            "pdf" => MsOffice.WdPdf, "docx" => MsOffice.WdDocx, "txt" => MsOffice.WdTxt, "rtf" => MsOffice.WdRtf,
            "odt" => MsOffice.WdOdt, "html" => MsOffice.WdHtml, "doc" => MsOffice.WdDoc,
            _ => throw new NotSupportedException(ext),
        };
        return MsOffice.WordConvert(job, ext, fmt, prepare);
    }

    public Task ToImages(Job job, string ext) => MsOffice.WordToImages(job, ext);
}
