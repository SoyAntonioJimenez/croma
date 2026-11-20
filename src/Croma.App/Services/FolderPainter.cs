using System.IO;
using System.Text;
using FolderHue.Core;

namespace FolderHue.App.Services;

public sealed record PaintResult(bool Success, string Message, string? Error = null);

/// <summary>
/// Applies a colour to a single folder through desktop.ini, merging with any
/// existing settings instead of overwriting them, and records what it changed.
/// </summary>
public static class FolderPainter
{
    private const string Section = "[.ShellClassInfo]";
    private const string IconResourceKey = "IconResource";
    private const string IconFileKey = "IconFile";
    private const string IconIndexKey = "IconIndex";

    public static PaintResult Apply(string folderPath, string colorId, bool record = true)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return new PaintResult(false, Loc.T("MsgNoFolder"));

            var color = Palette.Get(colorId);
            if (color.IsOriginal) return Restore(folderPath);

            string iniPath = Path.Combine(folderPath, "desktop.ini");
            bool existedBefore = File.Exists(iniPath);
            string icoPath = IconStore.EnsureIcon(color);

            List<string> lines;
            try { lines = ReadLines(iniPath); }
            catch (UnauthorizedAccessException) { return new PaintResult(false, Loc.T("MsgNoPermission")); }

            lines = MergeIcon(lines, icoPath);
            WriteLines(iniPath, lines);
            File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

            bool addedReadOnly = false;
            try
            {
                var attrs = File.GetAttributes(folderPath);
                if (!attrs.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(folderPath, attrs | FileAttributes.ReadOnly);
                    addedReadOnly = true;
                }
            }
            catch (Exception ex) { Log.Write($"No se pudo marcar la carpeta como solo lectura: {folderPath}", ex); }

            if (record) ConfigStore.SetFolder(folderPath, color.Id, !existedBefore, addedReadOnly);
            ShellNative.BumpFolderStamp(folderPath);
            ShellNative.NotifyItem(folderPath);
            ShellNative.NotifyDirectory(folderPath);
            string? parent = Path.GetDirectoryName(folderPath);
            if (!string.IsNullOrEmpty(parent)) ShellNative.NotifyDirectory(parent);
            ShellNative.NotifyChanged();

            Log.Write($"Carpeta pintada: {folderPath} → {color.Id}");
            return new PaintResult(true, Loc.F("MsgPainted", Path.GetFileName(folderPath), color.Name.ToLowerInvariant()));
        }
        catch (Exception ex)
        {
            Log.Write($"Error al pintar {folderPath}", ex);
            return new PaintResult(false, Loc.T("MsgCouldNotChange"), ex.Message);
        }
    }

    public static PaintResult Restore(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
                return new PaintResult(false, Loc.T("MsgNoFolder"));

            var entry = ConfigStore.FindFolder(folderPath);
            string iniPath = Path.Combine(folderPath, "desktop.ini");

            if (File.Exists(iniPath))
            {
                var lines = ReadLines(iniPath);
                var cleaned = RemoveIcon(lines);
                bool hasOtherContent = cleaned.Any(l =>
                    !string.IsNullOrWhiteSpace(l) && !l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));

                if (entry?.CreatedIni == true || !hasOtherContent)
                {
                    try { File.SetAttributes(iniPath, FileAttributes.Normal); } catch { }
                    File.Delete(iniPath);
                }
                else
                {
                    WriteLines(iniPath, cleaned);
                    File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);
                }
            }

            if (entry?.AddedReadOnly == true)
            {
                try
                {
                    var attrs = File.GetAttributes(folderPath);
                    if (attrs.HasFlag(FileAttributes.ReadOnly))
                        File.SetAttributes(folderPath, attrs & ~FileAttributes.ReadOnly);
                }
                catch (Exception ex) { Log.Write("No se pudo quitar el atributo de solo lectura.", ex); }
            }

            ConfigStore.SetFolder(folderPath, "original", false, false);
            ShellNative.BumpFolderStamp(folderPath);
            ShellNative.NotifyItem(folderPath);
            ShellNative.NotifyDirectory(folderPath);
            string? parent = Path.GetDirectoryName(folderPath);
            if (!string.IsNullOrEmpty(parent)) ShellNative.NotifyDirectory(parent);
            ShellNative.NotifyChanged();

            Log.Write($"Carpeta restaurada: {folderPath}");
            return new PaintResult(true, Loc.F("MsgRestored", Path.GetFileName(folderPath)));
        }
        catch (Exception ex)
        {
            Log.Write($"Error al restaurar {folderPath}", ex);
            return new PaintResult(false, Loc.T("MsgCouldNotRestore"), ex.Message);
        }
    }

    public static bool IsManaged(string folderPath) => ConfigStore.FindFolder(folderPath) is not null;

    private static List<string> ReadLines(string iniPath)
    {
        if (!File.Exists(iniPath)) return new List<string>();
        // File.ReadAllText honours the BOM, so UTF-16 desktop.ini files written by
        // Windows round-trip correctly.
        string text = File.ReadAllText(iniPath);
        return text.Replace("\r\n", "\n").Split('\n').ToList();
    }

    private static void WriteLines(string iniPath, List<string> lines)
    {
        if (File.Exists(iniPath))
        {
            try { File.SetAttributes(iniPath, FileAttributes.Normal); } catch { }
        }
        string text = string.Join("\r\n", lines).TrimEnd() + "\r\n";
        File.WriteAllText(iniPath, text, Encoding.Unicode);
    }

    private static int FindSection(List<string> lines, string section, out int end)
    {
        end = lines.Count;
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            bool isHeader = t.StartsWith('[') && t.EndsWith(']');
            if (start < 0)
            {
                if (isHeader && t.Equals(section, StringComparison.OrdinalIgnoreCase)) start = i;
            }
            else if (isHeader)
            {
                end = i;
                break;
            }
        }
        return start;
    }

    private static List<string> MergeIcon(List<string> lines, string icoPath)
    {
        var result = new List<string>(lines);
        int start = FindSection(result, Section, out int end);

        if (start < 0)
        {
            if (result.Count > 0 && !string.IsNullOrWhiteSpace(result[^1])) result.Add("");
            result.Add(Section);
            result.Add($"{IconResourceKey}={icoPath},0");
            return result;
        }

        // Drop legacy keys and any previous IconResource, then insert ours first.
        for (int i = end - 1; i > start; i--)
        {
            string t = result[i].Trim();
            if (t.StartsWith(IconResourceKey + "=", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(IconFileKey + "=", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(IconIndexKey + "=", StringComparison.OrdinalIgnoreCase))
                result.RemoveAt(i);
        }
        result.Insert(start + 1, $"{IconResourceKey}={icoPath},0");
        return result;
    }

    private static List<string> RemoveIcon(List<string> lines)
    {
        var result = new List<string>(lines);
        int start = FindSection(result, Section, out int end);
        if (start < 0) return result;

        bool removedAny = false;
        for (int i = end - 1; i > start; i--)
        {
            string t = result[i].Trim();
            if (t.StartsWith(IconResourceKey + "=", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(IconFileKey + "=", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith(IconIndexKey + "=", StringComparison.OrdinalIgnoreCase))
            {
                result.RemoveAt(i);
                removedAny = true;
            }
        }

        if (removedAny)
        {
            // If the section is left with no keys at all, drop the empty header too.
            int s2 = FindSection(result, Section, out int e2);
            if (s2 >= 0)
            {
                bool hasKeys = false;
                for (int i = s2 + 1; i < e2; i++)
                    if (!string.IsNullOrWhiteSpace(result[i])) { hasKeys = true; break; }
                if (!hasKeys) result.RemoveAt(s2);
            }
        }
        return result;
    }
}
