using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using FolderHue.Core;

namespace FolderHue.App.Services;

/// <summary>
/// Single place that performs colour changes, keeps the configuration, shortcuts
/// and the installed app icons consistent, and reports what happened.
/// </summary>
public sealed class ColorCoordinator
{
    public static ColorCoordinator Instance { get; } = new();

    public event Action<string>? StatusChanged;
    public event Action<FolderColor>? GlobalChanged;

    private void Status(string message) => StatusChanged?.Invoke(message);

    public async Task<ApplyOutcome> ApplyGlobalAsync(FolderColor color)
    {
        var cfg = ConfigStore.Current;
        Status(Loc.F("MsgApplying", color.Name));

        var outcome = await Task.Run(() => GlobalApplier.ApplyGlobal(color.Id, cfg.RestartExplorer));

        if (outcome.Success)
        {
            cfg.GlobalColor = color.Id;
            ConfigStore.Save();

            await Task.Run(() =>
            {
                if (cfg.ApplyToShortcuts)
                    ShortcutService.ApplyToFolderShortcuts(color, cfg.IncludeExplorerPin);
                else if (color.IsOriginal)
                    ShortcutService.RestoreFolderShortcuts();

                ShortcutService.CreateAppShortcuts(IconStore.EnsureAppIcon());
            });

            Status(outcome.Message);
            GlobalChanged?.Invoke(color);
        }
        else
        {
            Status(outcome.Message);
        }
        return outcome;
    }

    public async Task<ApplyOutcome> ApplyFolderAsync(string folderPath, FolderColor color)
    {
        var result = await Task.Run(() => FolderPainter.Apply(folderPath, color.Id));
        Status(result.Message);

        // The desktop only repaints when the shell reloads its icon cache, so make the
        // change actually visible instead of leaving a stale icon on screen.
        if (result.Success && ConfigStore.Current.RestartExplorer)
            await Task.Run(() => ShellNative.RestartExplorerWithCacheClear(TimeSpan.FromSeconds(1)));

        return new ApplyOutcome(result.Success, result.Message);
    }

    /// <summary>Restores every folder and the system icon to the Windows yellow.</summary>
    public async Task<ApplyOutcome> RestoreEverythingAsync()
    {
        Status(Loc.T("MsgRestoringEverything"));
        var folders = ConfigStore.Current.Folders.ToList();
        await Task.Run(() =>
        {
            foreach (var entry in folders)
            {
                try { if (Directory.Exists(entry.Value.Path)) FolderPainter.Restore(entry.Value.Path); }
                catch (Exception ex) { Log.Write("No se pudo restaurar " + entry.Value.Path, ex); }
            }
        });

        var outcome = await ApplyGlobalAsync(Palette.Original);
        ConfigStore.Current.GlobalColor = "original";
        ConfigStore.Save();
        return outcome;
    }

    /// <summary>Re-applies saved per-folder colours, e.g. after a system cleanup.</summary>
    public void RepairSavedFolders()
    {
        var folders = ConfigStore.Current.Folders.Values.ToList();
        if (folders.Count == 0) return;

        Task.Run(() =>
        {
            int repaired = 0;
            foreach (var entry in folders)
            {
                try
                {
                    if (!Directory.Exists(entry.Path)) continue;
                    string ini = Path.Combine(entry.Path, "desktop.ini");
                    var expected = IconStore.IconPath(Palette.Get(entry.Color));
                    bool needsFix = true;
                    if (File.Exists(ini))
                    {
                        try
                        {
                            string text = File.ReadAllText(ini);
                            needsFix = !text.Contains(expected, StringComparison.OrdinalIgnoreCase);
                        }
                        catch { }
                    }
                    if (needsFix)
                    {
                        FolderPainter.Apply(entry.Path, entry.Color);
                        repaired++;
                    }
                }
                catch (Exception ex) { Log.Write("No se pudo reparar " + entry.Path, ex); }
            }
            if (repaired > 0) Log.Write($"Carpetas reparadas al arrancar: {repaired}");
        });
    }
}
