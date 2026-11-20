using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using FolderHue.Core;

namespace FolderHue.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
            switch (cmd)
            {
                case "bake":
                    return Bake(args.Length > 1 ? args[1] : "assets/icons");
                case "sheet":
                    return Sheet(args.Length > 1 ? args[1] : "sheet.png",
                                 args.Length > 2 ? int.Parse(args[2]) : 128);
                case "list":
                    foreach (var c in Palette.All)
                        Console.WriteLine($"{c.Id,-12} {c.Name,-20} {c.Hex}  orig={c.IsOriginal}");
                    return 0;
                case "png":
                    return Png(args);
                case "shot":
                    return Shot(args.Length > 1 ? args[1] : "shot.png");
                case "shellicon":
                    return ShellIcon(args);
                default:
                    Console.WriteLine("""
                        folderhue-cli — utilidades del motor de color de FolderHue

                        Uso:
                          folderhue-cli bake [carpeta]        Genera todos los .ico del catálogo
                          folderhue-cli sheet [archivo.png] [px]  Hoja de contactos para revisión visual
                          folderhue-cli png <color> <px> <archivo>  Un PNG suelto
                          folderhue-cli list                  Lista el catálogo de colores
                        """);
                    return 0;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static int Bake(string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var color in Palette.All)
        {
            string path = Path.Combine(outDir, $"{color.Id}.ico");
            IconBaker.BakeIco(color, path);
            Console.WriteLine($"  {path}  ({new FileInfo(path).Length:N0} bytes)");
        }
        // Also a stable "current" copy used by the shell icon override.
        IconBaker.BakeIco(Palette.Original, Path.Combine(outDir, "current.ico"));
        Console.WriteLine($"Total: {Palette.All.Count + 1} iconos en {Path.GetFullPath(outDir)}");
        return 0;
    }

    private static int Png(string[] args)
    {
        var color = Palette.Get(args.Length > 1 ? args[1] : "red");
        int size = args.Length > 2 ? int.Parse(args[2]) : 256;
        string path = args.Length > 3 ? args[3] : $"{color.Id}_{size}.png";
        IconBaker.SavePng(color, size, path);
        Console.WriteLine($"Escrito {path}");
        return 0;
    }

    private static int ShellIcon(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Uso: shellicon <ruta> <salida.png>");
            return 2;
        }
        string path = args[1];
        using var bmp = ShellNative.GetShellIcon(path)
            ?? throw new InvalidOperationException("El shell no devolvió icono para " + path);
        // Upscale so the result is easy to inspect visually.
        using var big = new Bitmap(128, 128);
        using (var g = Graphics.FromImage(big))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(bmp, new Rectangle(0, 0, 128, 128));
        }
        big.Save(args[2], ImageFormat.Png);
        Console.WriteLine($"Icono del shell para {path} → {args[2]} (origen {bmp.Width}x{bmp.Height})");
        return 0;
    }

    private static int Shot(string outPath)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"Captura: {Path.GetFullPath(outPath)} ({bounds.Width}x{bounds.Height})");
        return 0;
    }

    private static int Sheet(string outPath, int px)
    {
        var colors = Palette.All;
        const int cols = 8, pad = 14, cell = 128, label = 26;
        int rows = (colors.Count + cols - 1) / cols;
        int w = cols * (cell + pad) + pad;
        int h = rows * (cell + label + pad) + pad;

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(255, 250, 250, 252));
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(255, 40, 40, 48));
        using var sub = new SolidBrush(Color.FromArgb(255, 120, 120, 130));

        for (int i = 0; i < colors.Count; i++)
        {
            int cx = pad + (i % cols) * (cell + pad);
            int cy = pad + (i / cols) * (cell + label + pad);
            using var icon = IconBaker.Render(colors[i], px);
            g.DrawImage(icon, new Rectangle(cx + (cell - px) / 2, cy, px, px));
            var sf = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(colors[i].Name, font, brush, new RectangleF(cx, cy + px + 2, cell, 14), sf);
            g.DrawString(colors[i].Hex, font, sub, new RectangleF(cx, cy + px + 12, cell, 14), sf);
        }
        bmp.Save(outPath, ImageFormat.Png);
        Console.WriteLine($"Hoja: {Path.GetFullPath(outPath)} ({w}x{h})");
        return 0;
    }
}
