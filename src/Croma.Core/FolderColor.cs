using System.Drawing;
using System.Globalization;

namespace FolderHue.Core;

/// <summary>
/// A folder colour definition. Each entry describes a vertical ramp that is mapped
/// onto the luminance of the original Windows folder icon, so the result keeps the
/// exact same shape, gloss and depth as the system icon.
/// </summary>
public sealed class FolderColor
{
    public required string Id { get; init; }

    /// <summary>English display name.</summary>
    public required string Name { get; init; }

    /// <summary>Spanish display name.</summary>
    public string NameEs { get; init; } = "";

    /// <summary>Hue in degrees (0-360). Ignored when <see cref="Saturation"/> is ~0.</summary>
    public double Hue { get; init; }
    /// <summary>Saturation 0..1 applied to the coloured parts of the icon.</summary>
    public double Saturation { get; init; }
    /// <summary>Luminance of the darkest part of the folder (the shaded tab and edges).</summary>
    public double DarkL { get; init; }
    /// <summary>Luminance of the brightest part of the folder (the lit face).</summary>
    public double LightL { get; init; }
    /// <summary>True for the untouched Windows yellow folder.</summary>
    public bool IsOriginal { get; init; }
    /// <summary>Optional group used to lay the palette out in the UI.</summary>
    public string Group { get; init; } = "default";

    /// <summary>
    /// When set, the hue sweeps from <see cref="Hue"/> to <see cref="Hue"/> + this value
    /// across the icon width, turning the folder into a gradient (used by "Rainbow").
    /// </summary>
    public double HueSpan { get; init; }

    public Color TopRgb => ColorFromHsl(Hue, Saturation, LightL);
    public Color BottomRgb => ColorFromHsl(Hue, Saturation, DarkL);

    /// <summary>Colour used for the swatch in the UI (middle of the ramp).</summary>
    public Color Swatch => IsOriginal
        ? Color.FromArgb(255, 255, 206, 84)
        : ColorFromHsl(Hue, Math.Max(Saturation, 0.02), (DarkL + LightL) / 2.0);

    public string Hex => IsOriginal ? "#FFCE54" : $"#{Swatch.R:X2}{Swatch.G:X2}{Swatch.B:X2}";

    public override string ToString() => Name;

    public static Color ColorFromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = l - c / 2.0;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    public static void ToHsl(Color color, out double h, out double s, out double l)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        double d = max - min;
        if (d < 1e-9) { h = 0; s = 0; return; }
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) * 60.0;
        else if (max == g) h = ((b - r) / d + 2) * 60.0;
        else h = ((r - g) / d + 4) * 60.0;
    }

    /// <summary>Builds a colour from a picked UI colour, deriving a sensible ramp.</summary>
    public static FolderColor FromPickedColor(Color picked, string name = "Custom")
    {
        ToHsl(picked, out double h, out double s, out double l);
        if (s < 0.06) s = 0;
        return new FolderColor
        {
            Id = "custom",
            Name = name,
            NameEs = name,
            Hue = h,
            Saturation = s,
            DarkL = Math.Clamp(l * 0.56, 0.05, 0.80),
            LightL = Math.Clamp(l + (1 - l) * 0.55, 0.12, 0.99),
            Group = "custom"
        };
    }
}

public static class Palette
{
    private static FolderColor C(string id, string name, string nameEs,
        double h, double s, double dark, double light, string group = "default")
        => new()
        {
            Id = id, Name = name, NameEs = nameEs, Hue = h, Saturation = s,
            DarkL = dark, LightL = light, Group = group
        };

    /// <summary>The untouched Windows folder.</summary>
    public static readonly FolderColor Original = new()
    {
        Id = "original",
        Name = "Original (yellow)",
        NameEs = "Original (amarillo)",
        IsOriginal = true,
        Group = "original"
    };

    /// <summary>40 entries: the untouched yellow plus 39 colours.</summary>
    public static readonly IReadOnlyList<FolderColor> All = new List<FolderColor>
    {
        Original,
        C("red",       "Red",        "Rojo",        2,   0.84, 0.33, 0.86),
        C("crimson",   "Crimson",    "Carmesí",     345, 0.78, 0.32, 0.80),
        C("pink",      "Pink",       "Rosa",        335, 0.72, 0.46, 0.93),
        C("lightpink", "Light pink", "Rosa claro",  338, 0.52, 0.62, 0.97),
        C("magenta",   "Magenta",    "Magenta",     310, 0.76, 0.35, 0.88),
        C("purple",    "Purple",     "Morado",      275, 0.66, 0.38, 0.85),
        C("violet",    "Violet",     "Violeta",     258, 0.68, 0.42, 0.89),
        C("indigo",    "Indigo",     "Índigo",      235, 0.64, 0.40, 0.78),
        C("navy",      "Navy",       "Azul marino", 222, 0.74, 0.33, 0.62),
        C("blue",      "Blue",       "Azul",        214, 0.86, 0.36, 0.87),
        C("sky",       "Sky",        "Celeste",     200, 0.80, 0.45, 0.91),
        C("cyan",      "Cyan",       "Cian",        187, 0.78, 0.39, 0.88),
        C("teal",      "Teal",       "Turquesa",    175, 0.70, 0.33, 0.84),
        C("emerald",   "Emerald",    "Esmeralda",   158, 0.72, 0.31, 0.82),
        C("green",     "Green",      "Verde",       132, 0.68, 0.35, 0.85),
        C("neon",      "Neon green", "Verde neón",  105, 0.95, 0.44, 0.90),
        C("lime",      "Lime",       "Lima",         95, 0.72, 0.42, 0.89),
        C("olive",     "Olive",      "Oliva",        75, 0.56, 0.34, 0.78),
        C("mustard",   "Mustard",    "Mostaza",      48, 0.74, 0.38, 0.82),
        C("gold",      "Gold",       "Oro",          44, 0.86, 0.40, 0.85),
        C("amber",     "Amber",      "Ámbar",        38, 0.86, 0.40, 0.87),
        C("orange",    "Orange",     "Naranja",      28, 0.90, 0.42, 0.89),
        C("tangerine", "Tangerine",  "Mandarina",    20, 0.92, 0.46, 0.91),
        C("coral",     "Coral",      "Coral",        10, 0.76, 0.43, 0.89),
        C("peach",     "Peach",      "Melocotón",    24, 0.72, 0.58, 0.95),
        C("brown",     "Brown",      "Marrón",       25, 0.58, 0.31, 0.64),
        C("chocolate", "Chocolate",  "Chocolate",    20, 0.52, 0.27, 0.55),
        C("bronze",    "Bronze",     "Bronce",       32, 0.50, 0.36, 0.68),
        C("wine",      "Wine",       "Vino",        350, 0.62, 0.33, 0.70),
        C("mint",      "Mint",       "Menta",       155, 0.62, 0.55, 0.94),
        C("lavender",  "Lavender",   "Lavanda",     268, 0.56, 0.50, 0.93),
        C("steel",     "Steel",      "Acero",       205, 0.18, 0.36, 0.78),
        C("silver",    "Silver",     "Plata",       210, 0.04, 0.52, 0.93),
        C("pearl",     "Pearl",      "Perla",       210, 0.06, 0.69, 0.98),
        C("white",     "White",      "Blanco",        0, 0.00, 0.67, 0.99),
        C("gray",      "Gray",       "Gris",        210, 0.03, 0.36, 0.82),
        C("graphite",  "Graphite",   "Grafito",     210, 0.05, 0.31, 0.60),
        C("black",     "Black",      "Negro",         0, 0.02, 0.12, 0.34),
        new FolderColor
        {
            Id = "rainbow", Name = "Rainbow", NameEs = "Arcoíris",
            Hue = 4, HueSpan = 330, Saturation = 0.82, DarkL = 0.42, LightL = 0.90,
            Group = "special"
        },
    };

    private static readonly Dictionary<string, FolderColor> Index =
        All.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);

    public static FolderColor Get(string? id)
        => id is not null && Index.TryGetValue(id, out var c) ? c : Original;

    public static bool Exists(string? id) => id is not null && Index.ContainsKey(id);

    public static string Describe() =>
        string.Join(", ", All.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Id}={c.Name}")));
}
