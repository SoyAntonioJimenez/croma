using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderHue.App.Services;

/// <summary>What FolderHue knows about one hand-painted folder.</summary>
public sealed class FolderEntry
{
    /// <summary>Original path with its real casing, so the UI can display it.</summary>
    public string Path { get; set; } = "";
    public string Color { get; set; } = "original";
    /// <summary>True when FolderHue created desktop.ini itself (so removal deletes it).</summary>
    public bool CreatedIni { get; set; }
    /// <summary>True when FolderHue set the folder's read-only attribute.</summary>
    public bool AddedReadOnly { get; set; }
    /// <summary>Folder names changed after the colour was applied are still tracked by this.</summary>
    public DateTime AppliedAt { get; set; } = DateTime.Now;
}

/// <summary>A shortcut whose icon FolderHue replaced, with what it looked like before.</summary>
public sealed class ShortcutEntry
{
    public string Path { get; set; } = "";
    public string OriginalIcon { get; set; } = "";
    public int OriginalIndex { get; set; }
    public bool HadIcon { get; set; }
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Colour applied to every folder through the shell icon override.</summary>
    public string GlobalColor { get; set; } = "original";
    public bool StartWithWindows { get; set; } = true;
    public bool RestartExplorer { get; set; } = true;
    public bool ApplyToShortcuts { get; set; } = true;
    public bool IncludeExplorerPin { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool FirstRunDone { get; set; }
    /// <summary>True once the coloured Explorer shortcut is actually on the taskbar.</summary>
    public bool ExplorerPinned { get; set; }
    /// <summary>True when Windows refused the automatic pin, so we stop retrying.</summary>
    public bool AutoPinBlocked { get; set; }
    /// <summary>UI language: "en" or "es".</summary>
    public string Language { get; set; } = "en";

    public Dictionary<string, FolderEntry> Folders { get; set; } = new();
    public Dictionary<string, ShortcutEntry> Shortcuts { get; set; } = new();

    [JsonIgnore]
    public int FolderCount => Folders.Values.Count(f => f.Color != "original");
}

/// <summary>Loads and saves <see cref="AppConfig"/>, writing atomically.</summary>
public static class ConfigStore
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static AppConfig? _current;

    public static AppConfig Current
    {
        get
        {
            lock (Gate) return _current ??= Load();
        }
    }

    /// <summary>Normalised key used for folder lookups (case-insensitive, no trailing slash).</summary>
    public static string Key(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch { full = path; }
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
    }

    private static AppConfig Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile), JsonOptions);
                if (cfg is not null)
                {
                    cfg.Folders ??= new();
                    cfg.Shortcuts ??= new();
                    return cfg;
                }
            }
        }
        catch (Exception ex) { Log.Write("No se pudo leer la configuración; se recrea.", ex); }
        return new AppConfig();
    }

    public static void Save()
    {
        lock (Gate)
        {
            try
            {
                AppPaths.EnsureCreated();
                string tmp = AppPaths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
                File.Move(tmp, AppPaths.ConfigFile, true);
            }
            catch (Exception ex) { Log.Write("No se pudo guardar la configuración.", ex); }
        }
    }

    public static void Reload()
    {
        lock (Gate) _current = Load();
    }

    public static FolderEntry? FindFolder(string path)
    {
        var cfg = Current;
        return cfg.Folders.TryGetValue(Key(path), out var e) ? e : null;
    }

    public static void SetFolder(string path, string color, bool createdIni, bool addedReadOnly)
    {
        var cfg = Current;
        string key = Key(path);
        if (color == "original")
        {
            cfg.Folders.Remove(key);
        }
        else
        {
            if (!cfg.Folders.TryGetValue(key, out var e))
            {
                e = new FolderEntry { Path = Path.GetFullPath(path) };
                cfg.Folders[key] = e;
            }
            e.Path = Path.GetFullPath(path);
            e.Color = color;
            e.CreatedIni |= createdIni;
            e.AddedReadOnly |= addedReadOnly;
            e.AppliedAt = DateTime.Now;
        }
        Save();
    }
}

/// <summary>Registry values captured before FolderHue changed them, so restore is exact.</summary>
public sealed class RegistryBackup
{
    public bool Captured { get; set; }
    public string? ShellIcon3 { get; set; }
    public bool ShellIcon3Existed { get; set; }
    public string? DirectoryDefaultIcon { get; set; }
    public string? FolderDefaultIcon { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.Now;
}

public static class BackupStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static RegistryBackup Load()
    {
        try
        {
            if (File.Exists(AppPaths.BackupFile))
                return JsonSerializer.Deserialize<RegistryBackup>(File.ReadAllText(AppPaths.BackupFile), Options)
                       ?? new RegistryBackup();
        }
        catch (Exception ex) { Log.Write("No se pudo leer la copia de seguridad del registro.", ex); }
        return new RegistryBackup();
    }

    public static void Save(RegistryBackup backup)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.BackupFile, JsonSerializer.Serialize(backup, Options));
        }
        catch (Exception ex) { Log.Write("No se pudo guardar la copia de seguridad del registro.", ex); }
    }
}
