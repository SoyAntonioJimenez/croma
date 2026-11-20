using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FolderHue.Core;

/// <summary>
/// The application icon: a paint droplet with a rainbow gradient on a dark tile.
/// Deliberately not a folder, so it can never be mistaken for a Windows folder.
/// </summary>
public static class AppIconBuilder
{
    public static Bitmap Render(int size)
    {
        int s = 256;
        var master = new Bitmap(s, s, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(master))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            int pad = 10;
            var tile = new Rectangle(pad, pad, s - pad * 2, s - pad * 2);
            using var tilePath = RoundedRect(tile, 54);

            using (var bg = new LinearGradientBrush(tile,
                       Color.FromArgb(255, 30, 32, 42), Color.FromArgb(255, 13, 14, 19), 90f))
                g.FillPath(bg, tilePath);

            using (var glow = new GraphicsPath())
            {
                glow.AddEllipse(52, 60, 152, 152);
                using var pgb = new PathGradientBrush(glow)
                {
                    CenterColor = Color.FromArgb(60, 120, 90, 255),
                    SurroundColors = new[] { Color.FromArgb(0, 120, 90, 255) }
                };
                g.FillPath(pgb, glow);
            }

            using (var edge = new Pen(Color.FromArgb(52, 255, 255, 255), 2f))
                g.DrawPath(edge, tilePath);

            // A clean colour disc: unmistakable, and nothing like a Windows folder.
            var disc = new Rectangle(58, 58, 140, 140);
            using (var fill = new LinearGradientBrush(disc,
                       Color.FromArgb(255, 255, 214, 92), Color.FromArgb(255, 88, 220, 226), 0f))
            {
                fill.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(255, 255, 214, 92),
                        Color.FromArgb(255, 255, 122, 96),
                        Color.FromArgb(255, 226, 96, 214),
                        Color.FromArgb(255, 120, 126, 255),
                        Color.FromArgb(255, 88, 220, 226)
                    },
                    Positions = new[] { 0f, 0.26f, 0.52f, 0.76f, 1f }
                };
                g.FillEllipse(fill, disc);
            }

            using (var ring = new Pen(Color.FromArgb(90, 255, 255, 255), 3f))
                g.DrawEllipse(ring, disc);

            // Soft gloss on the upper-left of the disc.
            using (var gloss = new GraphicsPath())
            {
                gloss.AddEllipse(84, 80, 46, 34);
                using var gb = new PathGradientBrush(gloss)
                {
                    CenterColor = Color.FromArgb(90, 255, 255, 255),
                    SurroundColors = new[] { Color.FromArgb(0, 255, 255, 255) }
                };
                g.FillPath(gb, gloss);
            }
        }

        if (size == s) return master;

        var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g2 = Graphics.FromImage(dst))
        {
            g2.InterpolationMode = size < 32 ? InterpolationMode.HighQualityBilinear : InterpolationMode.HighQualityBicubic;
            g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g2.DrawImage(master, new Rectangle(0, 0, size, size), 0, 0, s, s, GraphicsUnit.Pixel, attrs);
        }
        master.Dispose();
        return dst;
    }

    public static byte[] BakeIco(int[]? sizes = null)
    {
        sizes ??= new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var list = new List<(int, byte[], bool)>();
        foreach (int size in sizes.OrderBy(v => v))
        {
            using var bmp = Render(size);
            bool png = size >= 256;
            list.Add((size, png ? Png(bmp) : Dib(bmp), png));
        }
        return IcoWriter.Write(list);
    }

    public static void Bake(string path, int[]? sizes = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, BakeIco(sizes));
    }

    private static byte[] Png(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] Dib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = (w + 31) / 32 * 4;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40); bw.Write(w); bw.Write(h * 2);
        bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(w * h * 4);
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (int y = h - 1; y >= 0; y--)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                bw.Write(row);
            }
        }
        finally { bmp.UnlockBits(data); }
        bw.Write(new byte[maskStride * h]);
        bw.Flush();
        return ms.ToArray();
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
