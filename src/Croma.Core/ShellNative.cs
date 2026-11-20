using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace FolderHue.Core;

/// <summary>
/// Thin, dependency-free wrapper over the handful of shell calls FolderHue needs:
/// asking Windows which icon it currently shows for a path, and telling it to refresh.
/// </summary>
public static class ShellNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    /// <summary>Asks the shell which icon it currently resolves for a directory.</summary>
    public static Bitmap? GetShellIcon(string path, bool large = true)
    {
        var shfi = new SHFILEINFO();
        uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        IntPtr res = SHGetFileInfoW(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
        try
        {
            using var ico = Icon.FromHandle(shfi.hIcon);
            var bmp = new Bitmap(ico.Width, ico.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.DrawImage(ico.ToBitmap(), 0, 0, ico.Width, ico.Height);
            return bmp;
        }
        finally { DestroyIcon(shfi.hIcon); }
    }

    /// <summary>The generic folder icon, independent of any per-folder customisation.</summary>
    public static Bitmap? GetGenericFolderIcon(int size = 32)
    {
        var shfi = new SHFILEINFO();
        IntPtr res = SHGetFileInfoW("folder", FILE_ATTRIBUTE_DIRECTORY, ref shfi,
            (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_USEFILEATTRIBUTES | SHGFI_LARGEICON);
        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
        try
        {
            using var ico = Icon.FromHandle(shfi.hIcon);
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(ico.ToBitmap(), new Rectangle(0, 0, size, size));
            }
            return bmp;
        }
        finally { DestroyIcon(shfi.hIcon); }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNE_UPDATEDIR = 0x00001000;
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNF_IDLIST = 0x0000;
    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_FLUSH = 0x1000;
    private const uint SHCNF_FLUSHASYNC = 0x2000;

    /// <summary>Broadcasts "icons changed" to Explorer, without touching running windows.</summary>
    public static void NotifyChanged()
        => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Tells Explorer that a specific directory's appearance changed.</summary>
    public static void NotifyDirectory(string path)
    {
        IntPtr p = Marshal.StringToHGlobalUni(path);
        try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW | SHCNF_FLUSH, p, IntPtr.Zero); }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>Tells Explorer that one item changed: the key call to refresh a folder's own icon.</summary>
    public static void NotifyItem(string path)
    {
        IntPtr p = Marshal.StringToHGlobalUni(path);
        try { SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, p, IntPtr.Zero); }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>
    /// Explorer keys a folder's cached icon on its change stamp, so rewriting desktop.ini
    /// alone is not enough: toggling the read-only bit makes it re-read the custom icon.
    /// </summary>
    public static void BumpFolderStamp(string folder)
    {
        try
        {
            var attrs = File.GetAttributes(folder);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(folder, attrs & ~FileAttributes.ReadOnly);
                File.SetAttributes(folder, attrs);
            }
            else
            {
                File.SetAttributes(folder, attrs | FileAttributes.ReadOnly);
            }
        }
        catch { }
    }

    /// <summary>Removes the shell''s per-user icon caches so new icons are picked up.</summary>
    public static int ClearIconCache()
    {
        int removed = 0;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Explorer");
        if (!Directory.Exists(dir)) return 0;
        foreach (string file in Directory.EnumerateFiles(dir, "iconcache_*.db"))
        {
            try { File.Delete(file); removed++; } catch { /* in use: Explorer rebuilds it anyway */ }
        }
        return removed;
    }

    /// <summary>Restarts Explorer so shell icon overrides take effect immediately.</summary>
    public static void RestartExplorer(TimeSpan? wait = null)
    {
        KillExplorer();
        Thread.Sleep(wait ?? TimeSpan.FromSeconds(2));
        StartExplorer();
    }

    /// <summary>
    /// Explorer keeps the icon cache database files open, so they can only be deleted
    /// once it is not running. Killing it first is what makes new folder icons appear.
    /// </summary>
    public static void RestartExplorerWithCacheClear(TimeSpan? wait = null)
    {
        KillExplorer();
        Thread.Sleep(wait ?? TimeSpan.FromSeconds(1.5));
        int removed = ClearIconCache();
        Log?.Invoke($"Caché de iconos: {removed} archivo(s) liberados.");
        NotifyChanged();
        StartExplorer();
    }

    /// <summary>Optional hook so the core assembly can report progress without a logger.</summary>
    public static Action<string>? Log { get; set; }

    public static void KillExplorer()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { }
        }
    }

    public static void StartExplorer()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch { }
    }
}
