using System.IO;
using FolderHue.Core;

namespace FolderHue.App.Services;

/// <summary>Sets up (and tears down) everything FolderHue needs on a fresh machine.</summary>
public static class Installer
{
    public static void RunFirstTimeSetup(bool silent)
    {
        try
        {
            AppPaths.EnsureCreated();
            IconStore.EnsureAll();
            IconStore.ExportUserIcons();

            if (!ContextMenuInstaller.IsInstalled())
                ContextMenuInstaller.Install();

            var cfg = ConfigStore.Current;
            StartupService.SetEnabled(cfg.StartWithWindows);

            // Trust what Windows actually has, not only what we last saved.
            string detected = GlobalApplier.DetectCurrentColorId();
            if (!string.Equals(detected, cfg.GlobalColor, StringComparison.OrdinalIgnoreCase))
            {
                cfg.GlobalColor = detected;
                ConfigStore.Save();
            }

            var activeColor = Core.Palette.Get(cfg.GlobalColor);
            ShortcutService.CreateAppShortcuts(IconStore.EnsureAppIcon());

            if (!cfg.FirstRunDone)
            {
                cfg.FirstRunDone = true;
                // A brand-new install starts on the untouched Windows yellow.
                if (cfg.GlobalColor == "original" && !silent)
                    StartupService.SetEnabled(cfg.StartWithWindows);
                ConfigStore.Save();
            }

            Log.Write("Configuración inicial completada.");
        }
        catch (Exception ex)
        {
            Log.Write("Fallo en la configuración inicial", ex);
        }
    }

    /// <summary>Removes menus, autostart, shortcuts and restores the system icon.</summary>
    public static void Uninstall()
    {
        try
        {
            ContextMenuInstaller.Uninstall();
            StartupService.SetEnabled(false);
            ShortcutService.RemoveAppShortcuts();
            ShortcutService.RestoreFolderShortcuts();

            var outcome = GlobalApplier.ApplyGlobal("original", restartExplorer: true);
            Log.Write("Desinstalación: " + outcome.Message);

            ConfigStore.Current.GlobalColor = "original";
            ConfigStore.Save();
        }
        catch (Exception ex)
        {
            Log.Write("Fallo al desinstalar", ex);
        }
    }
}
