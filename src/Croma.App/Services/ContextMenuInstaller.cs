using System.IO;
using Microsoft.Win32;
using FolderHue.Core;

namespace FolderHue.App.Services;

/// <summary>
/// Registers the "Color de carpeta" entry in the folder context menu: a native
/// cascading menu with every colour and its own folder icon, plus the visual picker.
/// </summary>
public static class ContextMenuInstaller
{
    private const string DirectoryVerb = @"Software\Classes\Directory\shell\CromaFolder";
    private const string BackgroundVerb = @"Software\Classes\Directory\Background\shell\CromaFolder";
    private const string DriveVerb = @"Software\Classes\Drive\shell\CromaFolder";

    /// <summary>Bumped whenever the menu layout changes, so existing installs refresh.</summary>
    private const int MenuSchema = 10;

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DirectoryVerb);
        if (key is null) return false;
        return key.GetValue("FolderHueMenu") is int v && v == MenuSchema;
    }

    public static void Install()
    {
        // Remove every earlier spelling of the verb so no stale entry can survive.
        foreach (string legacy in new[]
                 {
                     @"Software\Classes\Directory\shell\FolderHue",
                     @"Software\Classes\Directory\Background\shell\FolderHue",
                     @"Software\Classes\Drive\shell\FolderHue",
                     @"Software\Classes\Folder\shell\FolderHue",

                 })
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(legacy, false); } catch { }
        }

        string exe = AppPaths.ExePath;
        string icon = IconStore.EnsureAppIcon();

        try
        {
            WriteMenu(DirectoryVerb, Loc.T("MenuFolder"), icon, "%1", exe, allowMulti: true);
            WriteMenu(BackgroundVerb, Loc.T("MenuThisFolder"), icon, "%V", exe, allowMulti: false);
            WriteMenu(DriveVerb, Loc.T("MenuThisDrive"), icon, "%1", exe, allowMulti: false);
            Log.Write($"Menú contextual instalado (esquema {MenuSchema}).");
        }
        catch (Exception ex)
        {
            Log.Write("No se pudo instalar el menú contextual", ex);
        }
    }

    /// <summary>
    /// One plain verb with a direct command: the most compatible form Windows offers.
    /// It opens the visual picker where the menu was, which is the whole colour chooser.
    /// </summary>
    private static void WriteMenu(string keyPath, string caption, string icon, string token,
        string exe, bool allowMulti)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, true);
        if (key is null) return;

        foreach (string child in key.GetSubKeyNames())
        {
            try { key.DeleteSubKeyTree(child, false); } catch { }
        }

        key.SetValue(null, caption, RegistryValueKind.String);
        key.SetValue("MUIVerb", caption, RegistryValueKind.String);
        key.SetValue("Icon", icon, RegistryValueKind.String);
        key.SetValue("Position", "top", RegistryValueKind.String);
        key.SetValue("MultiSelectModel", allowMulti ? "Player" : "Single", RegistryValueKind.String);
        key.SetValue("FolderHueMenu", MenuSchema, RegistryValueKind.DWord);

        using var command = key.CreateSubKey("command", true);
        command?.SetValue(null, $"\"{exe}\" --pick \"{token}\"", RegistryValueKind.String);

        Log.Write($"Verb ready: {keyPath} → --pick {token}");
    }
    private static string Slug(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.Length == 0 ? "grupo" : sb.ToString();
    }

    public static void Uninstall()
    {
        foreach (string path in new[] { DirectoryVerb, BackgroundVerb, DriveVerb })
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
            catch (Exception ex) { Log.Write($"No se pudo quitar el menú {path}", ex); }
        }
        Log.Write("Menú contextual eliminado.");
    }
}

/// <summary>Keeps FolderHue starting with Windows, via the per-user Run key.</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FolderHue";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Contains("FolderHue", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null) return;
            if (enabled)
                key.SetValue(ValueName, $"\"{AppPaths.ExePath}\" --tray", RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, false);
            Log.Write($"Inicio con Windows: {(enabled ? "activado" : "desactivado")}");
        }
        catch (Exception ex) { Log.Write("No se pudo cambiar el inicio con Windows", ex); }
    }
}

/// <summary>Groups the palette into ordered rows for menus and the picker.</summary>
public static class PaletteGroups
{
    public sealed record Group(string Name, IReadOnlyList<FolderColor> Colors);

    public static List<Group> Build()
    {
        var groups = new List<Group>
        {
            new("Basics", Palette.All.Where(c => c.IsOriginal).ToList()),
            new("Warm", Palette.All.Where(c => !c.IsOriginal && c.HueSpan == 0 && c.Saturation >= 0.2 &&
                (c.Hue < 70 || c.Hue >= 330)).OrderBy(c => c.Hue < 180 ? c.Hue + 360 : c.Hue).ToList()),
            new("Cool", Palette.All.Where(c => !c.IsOriginal && c.HueSpan == 0 && c.Saturation >= 0.2 &&
                c.Hue >= 70 && c.Hue < 330).OrderBy(c => c.Hue).ToList()),
            new("Neutral", Palette.All.Where(c => !c.IsOriginal && c.HueSpan == 0 && c.Saturation < 0.2).ToList()),
            new("Special", Palette.All.Where(c => c.HueSpan != 0).ToList()),
        };
        return groups.Where(g => g.Colors.Count > 0).ToList();
    }

    /// <summary>Flat, hue-ordered list used by the visual picker grid.</summary>
    public static List<FolderColor> Ordered()
    {
        var list = new List<FolderColor>();
        foreach (var group in Build())
            list.AddRange(group.Colors);
        return list;
    }
}
