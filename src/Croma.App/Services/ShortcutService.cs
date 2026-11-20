using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FolderHue.Core;

namespace FolderHue.App.Services;

/// <summary>
/// Rewrites the icon of .lnk shortcuts that point at folders (desktop, Start menu
/// and taskbar pins) and remembers how to put them back.
/// </summary>
public static class ShortcutService
{
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass { }

    private static IShellLinkW NewLink() => (IShellLinkW)(object)new ShellLinkCoClass();

    /// <summary>Paths searched when recolouring folder shortcuts.</summary>
    private static IEnumerable<string> AllShortcuts()
    {
        var roots = new List<string>();
        void Add(string p) { if (Directory.Exists(p)) roots.Add(p); }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));

        // Taskbar pins live here on Windows versions that still use shortcut files.
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Add(Path.Combine(appData, "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"));
        Add(Path.Combine(appData, "Microsoft", "Internet Explorer", "Quick Launch"));

        foreach (string root in roots)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0
                });
            }
            catch { continue; }

            foreach (string file in files) yield return file;
        }
    }

    private static string? ReadTarget(string lnkPath)
    {
        try
        {
            var link = NewLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.ToString();
        }
        catch { return null; }
    }

    private static (string Path, int Index) ReadIcon(string lnkPath)
    {
        try
        {
            var link = NewLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            link.GetIconLocation(sb, sb.Capacity, out int index);
            return (sb.ToString(), index);
        }
        catch { return ("", 0); }
    }

    private static bool WriteIcon(string lnkPath, string iconPath, int index)
    {
        try
        {
            var link = NewLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            link.SetIconLocation(iconPath, index);
            ((IPersistFile)link).Save(lnkPath, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"No se pudo cambiar el icono del acceso directo {lnkPath}", ex);
            return false;
        }
    }

    /// <summary>Recolours every shortcut that points at a folder (or at explorer.exe).</summary>
    public static int ApplyToFolderShortcuts(FolderColor color, bool includeExplorerPin)
    {
        if (color.IsOriginal) return RestoreFolderShortcuts();

        string ico = IconStore.EnsureIcon(color);
        var cfg = ConfigStore.Current;
        int changed = 0;

        foreach (string lnk in AllShortcuts())
        {
            string? target = ReadTarget(lnk);
            if (string.IsNullOrEmpty(target)) continue;

            bool isFolder = Directory.Exists(target);
            bool isExplorer = !isFolder && includeExplorerPin &&
                              Path.GetFileName(target).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);

            if (!isFolder && !isExplorer) continue;

            string key = lnk.ToLowerInvariant();
            if (!cfg.Shortcuts.ContainsKey(key))
            {
                var (oldPath, oldIndex) = ReadIcon(lnk);
                cfg.Shortcuts[key] = new ShortcutEntry
                {
                    Path = lnk,
                    OriginalIcon = oldPath,
                    OriginalIndex = oldIndex,
                    HadIcon = !string.IsNullOrEmpty(oldPath)
                };
            }
            if (WriteIcon(lnk, ico, 0)) changed++;
        }

        ConfigStore.Save();
        Log.Write($"Accesos directos recoloreados: {changed}");
        return changed;
    }

    /// <summary>Puts every shortcut icon back the way it was.</summary>
    public static int RestoreFolderShortcuts()
    {
        var cfg = ConfigStore.Current;
        int restored = 0;
        foreach (var pair in cfg.Shortcuts.ToList())
        {
            var entry = pair.Value;
            if (!File.Exists(entry.Path)) { cfg.Shortcuts.Remove(pair.Key); continue; }
            if (string.IsNullOrEmpty(entry.OriginalIcon)) continue;
            if (WriteIcon(entry.Path, entry.OriginalIcon, entry.OriginalIndex))
            {
                restored++;
                cfg.Shortcuts.Remove(pair.Key);
            }
        }
        ConfigStore.Save();
        Log.Write($"Accesos directos restaurados: {restored}");
        return restored;
    }

    /// <summary>Creates or refreshes FolderHue's own Desktop and Start Menu shortcuts.</summary>
    public static void CreateAppShortcuts(string iconPath)
    {
        try
        {
            string exe = AppPaths.ExePath;
            string workDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Croma.lnk");
            CreateShortcut(desktop, exe, "", iconPath, 0, "Croma", workDir);

            string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            if (Directory.Exists(programs))
                CreateShortcut(Path.Combine(programs, "Croma.lnk"), exe, "", iconPath, 0, "Croma", workDir);

            // Quick Launch copies (old taskbar pins) follow the same colour.
            string quickLaunch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Internet Explorer", "Quick Launch");
            if (Directory.Exists(quickLaunch))
                CreateShortcut(Path.Combine(quickLaunch, "Croma.lnk"), exe, "", iconPath, 0, "Croma", workDir);

            Log.Write("App shortcuts updated with the active colour.");
        }
        catch (Exception ex) { Log.Write("Could not create FolderHue shortcuts", ex); }
    }

    /// <summary>Rewrites any pinned shortcut that launches FolderHue so it carries the colour.</summary>
    public static int RecolorAppPins(string iconPath)
    {
        int changed = 0;
        foreach (string lnk in PinnedShortcuts())
        {
            string? target = ReadTarget(lnk);
            if (target is null) continue;
            if (!Path.GetFileName(target).Equals("FolderHue.exe", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(target).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            if (WriteIcon(lnk, iconPath, 0)) changed++;
        }
        return changed;
    }

    private static IEnumerable<string> PinnedShortcuts()
    {
        var roots = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Internet Explorer", "Quick Launch"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        foreach (string root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 });
            }
            catch { continue; }
            foreach (string f in files) yield return f;
        }
    }

    public static void RemoveAppShortcuts()
    {
        foreach (var dir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
                 })
        {
            try
            {
                string path = Path.Combine(dir, "Croma.lnk");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Log.Write("Could not delete a shortcut", ex); }
        }
    }

    /// <summary>
    /// A ready-to-pin File Explorer shortcut carrying the current colour. Windows does
    /// not allow pinning programmatically, so the user pins it once from its context menu.
    /// </summary>
    public static string ExplorerShortcutPath => Path.Combine(AppPaths.Root, "FolderHue Explorer.lnk");

    public static void CreateExplorerShortcut(FolderColor color)
    {
        try
        {
            string icon = color.IsOriginal ? IconStore.EnsureAppIcon() : IconStore.EnsureIcon(color);
            string explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            AppPaths.EnsureCreated();
            CreateShortcut(ExplorerShortcutPath, explorer, "", icon, 0, "FolderHue Explorer", "");

            // A copy in Pictures, where the user asked for it, plus Start menu for pinning.
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(pictures))
                CreateShortcut(Path.Combine(pictures, "FolderHue Explorer.lnk"), explorer, "", icon, 0,
                    "FolderHue Explorer", "");

            Log.Write($"Explorer shortcut ready to pin ({color.Id}).");
        }
        catch (Exception ex) { Log.Write("Could not create the Explorer shortcut", ex); }
    }

    public static bool CreateShortcut(string lnkPath, string target, string arguments, string iconPath,
        int iconIndex, string description, string workingDir)
    {
        try
        {
            var link = NewLink();
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(workingDir);
            if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
            if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, iconIndex);
            ((IPersistFile)link).Save(lnkPath, true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"No se pudo crear el acceso directo {lnkPath}", ex);
            return false;
        }
    }
}
