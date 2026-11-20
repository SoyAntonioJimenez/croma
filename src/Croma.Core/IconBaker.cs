using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FolderHue.Core;

/// <summary>
/// Extracts the real Windows folder icon and re-tints it, keeping its exact
/// geometry, gloss and anti-aliasing, then bakes the result into a multi-size .ico.
/// </summary>
public static class IconBaker
{
    /// <summary>Sizes stored inside every generated .ico.</summary>
    public static readonly int[] StandardSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    private static readonly object Gate = new();
    private static Bitmap? _source;
    private static double _srcMinL, _srcMaxL, _srcRefS;
    private static string? _sourcePath;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHDefExtractIconW(string pszIconFile, int iIndex, uint uFlags,
        IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIconSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>Where the pristine Windows folder icon is coming from (for the About/UI).</summary>
    public static string SourceDescription => _sourcePath ?? "shell32.dll";

    /// <summary>The untouched Windows folder bitmap, 256x256.</summary>
    public static Bitmap Source
    {
        get
        {
            lock (Gate)
            {
                if (_source is null) LoadSource();
                return _source!;
            }
        }
    }

    private static void LoadSource()
    {
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        // shell32.dll,-3 / ,3 is the classic "folder" slot that Windows 11 renders
        // with the modern fluent folder artwork.
        var candidates = new[]
        {
            (Path.Combine(sys, "shell32.dll"), 3),
            (Path.Combine(sys, "imageres.dll"), -112),
            (Path.Combine(sys, "shell32.dll"), -1),
        };

        Exception? last = null;
        foreach (var (file, index) in candidates)
        {
            try
            {
                var bmp = ExtractIconBitmap(file, index, 256);
                _source = bmp;
                _sourcePath = $"{Path.GetFileName(file)},{index}";
                AnalyseSource(bmp);
                return;
            }
            catch (Exception ex) { last = ex; }
        }
        throw new InvalidOperationException("No se pudo extraer el icono del sistema.", last);
    }

    /// <summary>Computes the luminance window used to normalise the tint ramp.</summary>
    private static void AnalyseSource(Bitmap bmp)
    {
        var samples = new List<double>(65536);
        double satSum = 0;
        int satCount = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.A < 200) continue;
                FolderColor.ToHsl(c, out _, out double s, out double l);
                samples.Add(l);
                if (s > 0.5) { satSum += s; satCount++; }
            }
        }
        if (samples.Count == 0) { _srcMinL = 0.4; _srcMaxL = 0.9; _srcRefS = 0.95; return; }
        samples.Sort();
        // Robust window: ignore the extreme 1% so stray pixels do not flatten the ramp.
        _srcMinL = samples[(int)(samples.Count * 0.012)];
        _srcMaxL = samples[(int)(samples.Count * 0.988)];
        if (_srcMaxL - _srcMinL < 0.02) _srcMaxL = _srcMinL + 0.02;
        _srcRefS = satCount > 0 ? satSum / satCount : 0.95;
    }

    private static Bitmap ExtractIconBitmap(string file, int index, int size)
    {
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        uint iconSize = (uint)(size | (size << 16));
        int hr = SHDefExtractIconW(file, index, 0, large, small, iconSize);
        IntPtr h = large[0] != IntPtr.Zero ? large[0] : small[0];
        if (h == IntPtr.Zero) throw new InvalidOperationException($"SHDefExtractIcon falló (0x{hr:X8}) para {file},{index}");
        try
        {
            using var ico = Icon.FromHandle(h);
            using var raw = ico.ToBitmap();
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceOver;
                g.DrawImage(raw, new Rectangle(0, 0, size, size));
            }
            return bmp;
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero && small[0] != large[0]) DestroyIcon(small[0]);
        }
    }

    /// <summary>Renders the folder icon for <paramref name="color"/> at an arbitrary size.</summary>
    public static Bitmap Render(FolderColor color, int size)
    {
        var src = Source;
        Bitmap tinted = color.IsOriginal || color.Id == "original"
            ? (Bitmap)src.Clone()
            : Tint(src, color);

        if (size == tinted.Width) return tinted;

        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.InterpolationMode = size < 32 ? InterpolationMode.HighQualityBilinear : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(tinted, new Rectangle(0, 0, size, size), 0, 0, tinted.Width, tinted.Height,
                GraphicsUnit.Pixel, attrs);
        }
        tinted.Dispose();
        return dst;
    }

    /// <summary>Maps the source luminance ramp onto the target colour ramp.</summary>
    private static Bitmap Tint(Bitmap src, FolderColor color)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        bool achromatic = color.Saturation < 0.07;

        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var srcData = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dstData = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(srcData.Stride) * src.Height;
        var buffer = new byte[bytes];
        var outBuffer = new byte[bytes];
        Marshal.Copy(srcData.Scan0, buffer, 0, bytes);

        int stride = srcData.Stride;
        double range = _srcMaxL - _srcMinL;

        for (int y = 0; y < src.Height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < src.Width; x++)
            {
                int i = row + x * 4;
                byte b = buffer[i], g = buffer[i + 1], r = buffer[i + 2], a = buffer[i + 3];
                if (a == 0)
                {
                    outBuffer[i] = 0; outBuffer[i + 1] = 0; outBuffer[i + 2] = 0; outBuffer[i + 3] = 0;
                    continue;
                }

                FolderColor.ToHsl(Color.FromArgb(r, g, b), out _, out double s, out double l);
                double n = Math.Clamp((l - _srcMinL) / range, 0, 1);
                // Slight S-curve keeps the mid-tones from flattening out.
                n = n * n * (3 - 2 * n) * 0.55 + n * 0.45;

                double lOut = color.DarkL + (color.LightL - color.DarkL) * n;
                double sOut = achromatic
                    ? color.Saturation
                    : color.Saturation * Math.Clamp(s / Math.Max(_srcRefS, 0.35), 0.0, 1.0);

                double hue = color.HueSpan == 0
                    ? color.Hue
                    : color.Hue + color.HueSpan * (x / (double)(src.Width - 1));

                Color c = FolderColor.ColorFromHsl(hue, sOut, lOut);
                outBuffer[i] = c.B;
                outBuffer[i + 1] = c.G;
                outBuffer[i + 2] = c.R;
                outBuffer[i + 3] = a;
            }
        }

        Marshal.Copy(outBuffer, 0, dstData.Scan0, bytes);
        src.UnlockBits(srcData);
        dst.UnlockBits(dstData);
        return dst;
    }

    /// <summary>Renders every standard size for a colour.</summary>
    public static Dictionary<int, Bitmap> RenderAll(FolderColor color)
    {
        var result = new Dictionary<int, Bitmap>();
        foreach (int size in StandardSizes) result[size] = Render(color, size);
        return result;
    }

    /// <summary>Bakes a multi-resolution .ico file for the given colour.</summary>
    public static byte[] BakeIcoBytes(FolderColor color, int[]? sizes = null)
    {
        sizes ??= StandardSizes;
        var bitmaps = new List<(int Size, byte[] Data, bool Png)>();
        foreach (int size in sizes.OrderBy(s => s))
        {
            using var bmp = Render(color, size);
            bool asPng = size >= 256; // PNG keeps the 256px entry crisp; DIB below that for max compatibility.
            bitmaps.Add((size, asPng ? ToPng(bmp) : ToDib(bmp), asPng));
        }
        return IcoWriter.Write(bitmaps);
    }

    public static void BakeIco(FolderColor color, string path, int[]? sizes = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, BakeIcoBytes(color, sizes));
    }

    public static void SavePng(FolderColor color, int size, string path)
    {
        using var bmp = Render(color, size);
        bmp.Save(path, ImageFormat.Png);
    }

    public static Bitmap RenderPreview(FolderColor color, int size) => Render(color, size);

    private static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>32-bpp bottom-up DIB as embedded inside .ico files.</summary>
    private static byte[] ToDib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = (w + 31) / 32 * 4; // 1 bpp, dword aligned
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write(40);              // biSize
        bw.Write(w);               // biWidth
        bw.Write(h * 2);           // biHeight (image + mask)
        bw.Write((short)1);        // biPlanes
        bw.Write((short)32);       // biBitCount
        bw.Write(0);               // BI_RGB
        bw.Write(w * h * 4);       // biSizeImage
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (int y = h - 1; y >= 0; y--) // bottom-up
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                bw.Write(row);
            }
        }
        finally { bmp.UnlockBits(data); }

        bw.Write(new byte[maskStride * h]); // AND mask: fully opaque, alpha channel does the work
        bw.Flush();
        return ms.ToArray();
    }
}

/// <summary>Minimal, spec-compliant ICO container writer.</summary>
public static class IcoWriter
{
    public static byte[] Write(IReadOnlyList<(int Size, byte[] Data, bool Png)> images)
    {
        if (images.Count == 0) throw new ArgumentException("Se necesita al menos una imagen.");
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((short)0);              // reserved
        bw.Write((short)1);              // type: icon
        bw.Write((short)images.Count);

        int offset = 6 + 16 * images.Count;
        foreach (var img in images)
        {
            bw.Write((byte)(img.Size >= 256 ? 0 : img.Size)); // width
            bw.Write((byte)(img.Size >= 256 ? 0 : img.Size)); // height
            bw.Write((byte)0);            // palette count
            bw.Write((byte)0);            // reserved
            bw.Write((short)1);           // planes
            bw.Write((short)32);          // bpp
            bw.Write(img.Data.Length);
            bw.Write(offset);
            offset += img.Data.Length;
        }
        foreach (var img in images) bw.Write(img.Data);
        bw.Flush();
        return ms.ToArray();
    }
}
