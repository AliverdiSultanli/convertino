using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Convertino.Native;

#region COM interfaces

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItem
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(uint sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(SIZE size, int flags, out IntPtr phbm);
}

[StructLayout(LayoutKind.Sequential)]
struct SIZE { public int cx, cy; }

[ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDropTargetHelper
{
    void DragEnter(IntPtr hwndTarget, ComIDataObject ifcDataObject, ref POINT ppt, int dwEffect);
    void DragLeave();
    void DragOver(ref POINT ppt, int dwEffect);
    void Drop(ComIDataObject ifcDataObject, ref POINT ppt, int dwEffect);
    void Show(bool fShow);
}

[ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
class DragDropHelperClass { }

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IComServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellBrowser
{
    void GetWindow(out IntPtr phwnd);
    void ContextSensitiveHelp(bool fEnterMode);
    void InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
    void SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
    void RemoveMenusSB(IntPtr hmenuShared);
    void SetStatusTextSB(IntPtr pszStatusText);
    void EnableModelessSB(bool fEnable);
    void TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
    void BrowseObject(IntPtr pidl, uint wFlags);
    void GetViewStateStream(uint grfMode, IntPtr ppStrm);
    void GetControlWindow(uint id, out IntPtr phwnd);
    void SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr pret);
    [return: MarshalAs(UnmanagedType.Interface)]
    IShellView QueryActiveShellView();
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellView
{
    void GetWindow(out IntPtr phwnd);
    void ContextSensitiveHelp(bool fEnterMode);
    void TranslateAccelerator(IntPtr pmsg);
    void EnableModeless(bool fEnable);
    void UIActivate(uint uState);
    void Refresh();
    void CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
    void DestroyViewWindow();
    void GetCurrentInfo(IntPtr pfs);
    void AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
    void SaveViewState();
    void SelectItem(IntPtr pidlItem, uint uFlags);
    [PreserveSig]
    int GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.IDispatch)] out object ppv);
}

[ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
interface IShellWindows
{
    int Count { get; }
    [return: MarshalAs(UnmanagedType.IDispatch)] object Item([In] object index);
    [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
    void Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int plCookie);
    void RegisterPending(int lThreadId, [In] ref object pvarloc, [In] ref object pvarlocRoot, int swClass, out int plCookie);
    void Revoke(int lCookie);
    void OnNavigate(int lCookie, [In] ref object pvarLoc);
    void OnActivated(int lCookie, bool fActive);
    [return: MarshalAs(UnmanagedType.IDispatch)]
    object FindWindowSW([In] ref object pvarLoc, [In] ref object pvarLocRoot, int swClass, out int phwnd, int swfwOptions);
}

[ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
class ShellWindowsClass { }

#endregion

public static class Shell
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    /// <summary>Explorer's own thumbnail (or icon) for any path. Safe to call off the UI thread.</summary>
    public static BitmapSource? Thumbnail(string path, int px)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
            var factory = (IShellItemImageFactory)item;
            // SIIGBF_RESIZETOFIT(0) | SIIGBF_BIGGERSIZEOK(1)
            if (factory.GetImage(new SIZE { cx = px, cy = px }, 0x1, out hbm) != 0 || hbm == IntPtr.Zero) return null;
            return FromHBitmap(hbm);
        }
        catch { return null; }
        finally
        {
            if (hbm != IntPtr.Zero) Win32.DeleteObject(hbm);
        }
    }

    static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        if (Win32.GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0) return null;
        var bm = ds.dsBm;
        if (bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32)
        {
            var s = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            s.Freeze();
            return s;
        }
        int stride = bm.bmWidthBytes, h = bm.bmHeight, w = bm.bmWidth;
        var buf = new byte[stride * h];
        Marshal.Copy(bm.bmBits, buf, 0, buf.Length);
        bool bottomUp = ds.dsBmih.biHeight > 0;
        if (bottomUp)
        {
            var flipped = new byte[buf.Length];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(buf, y * stride, flipped, (h - 1 - y) * stride, stride);
            buf = flipped;
        }
        bool anyAlpha = false;
        for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, buf, stride);
        bmp.Freeze();
        return bmp;
    }

    static IDropTargetHelper? _dropHelper;

    static IDropTargetHelper? DropHelper
    {
        get
        {
            try { return _dropHelper ??= (IDropTargetHelper)new DragDropHelperClass(); }
            catch { return null; }
        }
    }

    public static void HelperEnter(IntPtr hwnd, IDataObject data, POINT pt, int effect)
    {
        try { if (data is ComIDataObject com) DropHelper?.DragEnter(hwnd, com, ref pt, effect); } catch { }
    }

    public static void HelperOver(POINT pt, int effect)
    {
        try { DropHelper?.DragOver(ref pt, effect); } catch { }
    }

    public static void HelperLeave()
    {
        try { DropHelper?.DragLeave(); } catch { }
    }

    public static void HelperDrop(IDataObject data, POINT pt, int effect)
    {
        try { if (data is ComIDataObject com) DropHelper?.Drop(com, ref pt, effect); } catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClipboardFormat(string format);

    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);

    static ushort _cfDropDescription;

    /// <summary>
    /// Caption under Explorer's drag image ("Convert to %1" + "PNG"). type: -1 = reset to default,
    /// 0 = none (⊘), 1 = copy, 6 = label.
    /// </summary>
    public static void SetDropDescription(IDataObject data, int type, string message = "", string insert = "")
    {
        if (data is not ComIDataObject com) return;
        try
        {
            if (_cfDropDescription == 0) _cfDropDescription = RegisterClipboardFormat("DropDescription");
            // DROPDESCRIPTION { int type; WCHAR szMessage[260]; WCHAR szInsert[260]; }
            const int size = 4 + 520 + 520;
            var h = GlobalAlloc(0x0042 /*GMEM_MOVEABLE|GMEM_ZEROINIT*/, (UIntPtr)size);
            var p = GlobalLock(h);
            Marshal.WriteInt32(p, type);
            var m = (message.Length > 259 ? message[..259] : message).ToCharArray();
            var ins = (insert.Length > 259 ? insert[..259] : insert).ToCharArray();
            Marshal.Copy(m, 0, p + 4, m.Length);
            Marshal.Copy(ins, 0, p + 4 + 520, ins.Length);
            GlobalUnlock(h);
            var fmt = new System.Runtime.InteropServices.ComTypes.FORMATETC
            {
                cfFormat = unchecked((short)_cfDropDescription),
                dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
            };
            var med = new System.Runtime.InteropServices.ComTypes.STGMEDIUM
            {
                tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
                unionmember = h,
            };
            com.SetData(ref fmt, ref med, true);
        }
        catch { }
    }

    public static void Reveal(string path)
    {
        try
        {
            if (Directory.Exists(path) && !File.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex) { Log.Error("reveal", ex); }
    }

    public static void Open(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("open", ex); }
    }

    static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static readonly Guid IID_IShellBrowser = typeof(IShellBrowser).GUID;
    static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    /// <summary>
    /// Files selected in the foreground Explorer window (active tab) or on the desktop.
    /// Must run on an STA thread. Returns empty when the foreground isn't Explorer.
    /// </summary>
    public static List<string> ExplorerSelection(IntPtr foreground)
    {
        var result = new List<string>();
        var root = Win32.GetAncestor(foreground, Win32.GA_ROOT);
        var cls = Win32.ClassOf(root);
        try
        {
            var windows = (IShellWindows)new ShellWindowsClass();
            if (cls == "CabinetWClass")
            {
                var activeTab = Win32.FindWindowEx(root, IntPtr.Zero, "ShellTabWindowClass", null);
                int n = windows.Count;
                for (int i = 0; i < n; i++)
                {
                    object? w = null;
                    try { w = windows.Item(i); } catch { }
                    if (w == null) continue;
                    try
                    {
                        dynamic dw = w;
                        if (new IntPtr((long)dw.HWND) != root) continue;
                        var browser = BrowserOf(w);
                        if (browser != null && activeTab != IntPtr.Zero)
                        {
                            browser.GetWindow(out var tabHwnd);
                            if (tabHwnd != activeTab) continue;
                        }
                        foreach (dynamic item in dw.Document.SelectedItems()) result.Add((string)item.Path);
                        break;
                    }
                    catch { }
                }
            }
            else if (cls is "Progman" or "WorkerW")
            {
                object loc = 0;
                object empty = null!;
                // SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1
                var disp = windows.FindWindowSW(ref loc, ref empty, 8, out _, 1);
                var browser = BrowserOf(disp);
                var view = browser?.QueryActiveShellView();
                if (view != null)
                {
                    var iid = IID_IDispatch;
                    if (view.GetItemObject(0 /*SVGIO_BACKGROUND*/, ref iid, out var folderView) == 0)
                    {
                        dynamic fv = folderView;
                        foreach (dynamic item in fv.SelectedItems()) result.Add((string)item.Path);
                    }
                }
            }
        }
        catch (Exception ex) { Log.Error("explorer selection", ex); }
        return result.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
    }

    static IShellBrowser? BrowserOf(object o)
    {
        try
        {
            var sp = (IComServiceProvider)o;
            var sid = SID_STopLevelBrowser;
            var iid = IID_IShellBrowser;
            return sp.QueryService(ref sid, ref iid, out var b) == 0 ? (IShellBrowser)b : null;
        }
        catch { return null; }
    }
}
