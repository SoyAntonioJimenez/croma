using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FolderHue.Core;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace FolderHue.App.Services;

/// <summary>Bakes, caches and hands out folder icons for the whole application.</summary>
public static class IconStore
{
    private static readonly ConcurrentDictionary<string, BitmapSource> PreviewCache = new();
    private static readonly object Gate = new();

    public static string IconPath(FolderColor color) => Path.Combine(AppPaths.IconDir, color.Id + ".ico");

    private static bool IsUsable(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 1024; }
        catch { return false; }
    }

    /// <summary>Makes sure a colour's .ico exists on disk and returns its path.</summary>
    public static string EnsureIcon(FolderColor color)
    {
        AppPaths.EnsureCreated();
        string path = IconPath(color);
        if (IsUsable(path)) return path;
        lock (Gate)
        {
            if (IsUsable(path)) return path;
            IconBaker.BakeIco(color, path);
            Log.Write($"Icono generado: {path}");
            return path;
        }
    }

    /// <summary>The application's own icon, used for menus and shortcuts.</summary>
    public static string EnsureAppIcon()
    {
        AppPaths.EnsureCreated();
        string path = AppPaths.AppIconPath;
        if (IsUsable(path)) return path;
        lock (Gate)
        {
            if (IsUsable(path)) return path;
            byte[] bytes = AppIconBuilder.BakeIco();
            byte[] baked = File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Croma.ico"))
                ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "Croma.ico"))
                : bytes;
            File.WriteAllBytes(path, baked);
            return path;
        }
    }

    public static void EnsureAll()
    {
        foreach (var color in Palette.All) EnsureIcon(color);
        EnsureAppIcon();
    }

    /// <summary>Where the browsable, human-named copy of the icon set lives.</summary>
    public static string UserIconDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "FolderHue Icons");

    /// <summary>
    /// Publishes every colour as a numbered .ico in Pictures, so the icons can be picked
    /// by hand from any "Change icon…" dialog without digging into AppData.
    /// </summary>
    public static int ExportUserIcons()
    {
        int written = 0;
        try
        {
            Directory.CreateDirectory(UserIconDir);
            int index = 1;
            foreach (var color in Palette.All)
            {
                string target = Path.Combine(UserIconDir, $"{index:00} - {color.Name}.ico");
                string source = EnsureIcon(color);
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                {
                    File.Copy(source, target, true);
                    written++;
                }
                index++;
            }

            // A plain readme so the folder explains itself.
            string readme = Path.Combine(UserIconDir, "Read me.txt");
            if (!File.Exists(readme))
            {
                File.WriteAllText(readme,
                    "FolderHue — folder icons in every colour.\r\n\r\n" +
                    "Use them from any \"Change icon…\" dialog: right-click a shortcut or folder,\r\n" +
                    "Properties → Change icon → Browse, and pick the colour you want.\r\n\r\n" +
                    "FolderHue regenerates these files whenever you apply a colour.\r\n");
            }

            Log.Write($"Icon set published to {UserIconDir} ({written} updated).");
        }
        catch (Exception ex) { Log.Write("Could not publish the icon set", ex); }
        return written;
    }

    /// <summary>A frozen, WPF-ready bitmap of the folder in the given colour.</summary>
    public static BitmapSource Preview(FolderColor color, int size)
    {
        string key = $"{color.Id}:{size}";
        if (PreviewCache.TryGetValue(key, out var cached)) return cached;

        BitmapSource source;
        using (Bitmap bmp = IconBaker.Render(color, size))
            source = ToBitmapSource(bmp);

        if (PreviewCache.Count > 512) PreviewCache.Clear();
        PreviewCache[key] = source;
        return source;
    }

    public static void ClearPreviewCache() => PreviewCache.Clear();

    /// <summary>Converts a System.Drawing bitmap into a frozen WPF bitmap, alpha intact.</summary>
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var bytes = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var source = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96,
                PixelFormats.Bgra32, null, bytes, stride);
            source.Freeze();
            return source;
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Loads the embedded app icon for window chrome.</summary>
    public static BitmapSource AppIconImage()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/Croma.ico");
            var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource best = decoder.Frames[0];
            foreach (var frame in decoder.Frames)
                if (frame.PixelWidth > best.PixelWidth) best = frame;
            best.Freeze();
            return best;
        }
        catch
        {
            return Preview(Palette.Get("rainbow"), 64);
        }
    }

    /// <summary>Multi-colour gradient brush for the "Arcoíris" swatch.</summary>
    public static LinearGradientBrush RainbowBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 0)
        };
        for (int i = 0; i <= 6; i++)
        {
            var c = FolderColor.ColorFromHsl(i * 55 + 4, 0.82, 0.62);
            brush.GradientStops.Add(new GradientStop(
                System.Windows.Media.Color.FromRgb(c.R, c.G, c.B), i / 6.0));
        }
        brush.Freeze();
        return brush;
    }
}
