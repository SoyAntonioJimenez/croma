using System.IO;
using System.Security.Principal;
using Microsoft.Win32;
using FolderHue.Core;

namespace FolderHue.App.Services;

public sealed record ApplyOutcome(bool Success, string Message, bool Cancelled = false);

/// <summary>
/// Writes the machine-wide folder icon override. Windows ignores the per-user
/// equivalent for folders, so this part runs elevated.
/// </summary>
public static class GlobalApplier
{
    private const string ShellIconsPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons";
    private const string DirectoryIconPath = @"SOFTWARE\Classes\Directory\DefaultIcon";
    private const string FolderIconPath = @"SOFTWARE\Classes\Folder\DefaultIcon";
    private const string DefaultIconValue = @"%SystemRoot%\System32\shell32.dll,3";

    public static bool IsElevated
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>Runs inside the elevated helper process; returns a process exit code.</summary>
    public static int RunElevatedApply(string colorId, bool restartExplorer)
    {
        try
        {
            var color = Palette.Get(colorId);
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

            var backup = BackupStore.Load();
            if (!backup.Captured) Capture(hklm);

            if (color.IsOriginal)
            {
                using (var shellIcons = hklm.CreateSubKey(ShellIconsPath, true))
                    shellIcons?.DeleteValue("3", false);

                var current = BackupStore.Load();
                RestoreDefault(hklm, DirectoryIconPath, current.DirectoryDefaultIcon);
                RestoreDefault(hklm, FolderIconPath, current.FolderDefaultIcon);
                Log.Write("Icono global restaurado al original.");
            }
            else
            {
                string ico = IconStore.EnsureIcon(color);
                string value = ico + ",0";
                using (var shellIcons = hklm.CreateSubKey(ShellIconsPath, true))
                    shellIcons?.SetValue("3", value, RegistryValueKind.ExpandString);
                SetDefault(hklm, DirectoryIconPath, value);
                SetDefault(hklm, FolderIconPath, value);
                Log.Write($"Icono global aplicado: {color.Id} → {value}");
            }

            ShellNative.ClearIconCache();
            ShellNative.NotifyChanged();

            if (restartExplorer)
            {
                ShellNative.RestartExplorer(TimeSpan.FromSeconds(2));
                Log.Write("Explorador de Windows reiniciado.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Write("Fallo al aplicar el color global", ex);
            return 1;
        }
    }

    private static bool IsOurIcon(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(AppPaths.IconDir, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the machine icon override back, so the app never shows a colour that is
    /// not the one Windows is actually using.
    /// </summary>
    public static string DetectCurrentColorId()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(DirectoryIconPath);
            if (key?.GetValue(null) is not string value || string.IsNullOrWhiteSpace(value))
                return "original";

            string file = value.Split(',')[0].Trim();
            if (!file.Contains(AppPaths.IconDir, StringComparison.OrdinalIgnoreCase))
                return "original";

            string id = Path.GetFileNameWithoutExtension(file);
            return Palette.Exists(id) ? id : "original";
        }
        catch (Exception ex)
        {
            Log.Write("Could not read the system icon override", ex);
            return "original";
        }
    }

    private static void Capture(RegistryKey hklm)
    {
        var backup = new RegistryBackup { Captured = true };
        try
        {
            using var shellIcons = hklm.OpenSubKey(ShellIconsPath);
            object? v = shellIcons?.GetValue("3");
            // A value pointing at our own icon is not a "previous" state to keep.
            if (v is string s && !IsOurIcon(s))
            {
                backup.ShellIcon3Existed = true;
                backup.ShellIcon3 = s;
            }
        }
        catch { }

        string? directory = ReadDefault(hklm, DirectoryIconPath);
        string? folder = ReadDefault(hklm, FolderIconPath);
        backup.DirectoryDefaultIcon = IsOurIcon(directory) ? null : directory;
        backup.FolderDefaultIcon = IsOurIcon(folder) ? null : folder;
        BackupStore.Save(backup);
        Log.Write($"Copia de seguridad del registro guardada (Directory={backup.DirectoryDefaultIcon}).");
    }

    private static string? ReadDefault(RegistryKey hklm, string path)
    {
        try
        {
            using var key = hklm.OpenSubKey(path);
            return key?.GetValue(null) as string;
        }
        catch { return null; }
    }

    private static void SetDefault(RegistryKey hklm, string path, string value)
    {
        using var key = hklm.CreateSubKey(path, true);
        key?.SetValue(null, value, RegistryValueKind.ExpandString);
    }

    private static void RestoreDefault(RegistryKey hklm, string path, string? original)
    {
        // Never "restore" our own icon: fall back to the stock Windows value.
        string value = string.IsNullOrWhiteSpace(original) || IsOurIcon(original)
            ? DefaultIconValue
            : original!;
        SetDefault(hklm, path, value);
    }

    /// <summary>Asks Windows for elevation once and applies the colour machine-wide.</summary>
    public static ApplyOutcome ApplyGlobal(string colorId, bool restartExplorer)
    {
        var color = Palette.Get(colorId);

        if (IsElevated)
        {
            int code = RunElevatedApply(colorId, restartExplorer);
            return new ApplyOutcome(code == 0,
                code == 0 ? Loc.F("MsgSystemApplied", color.Name) : Loc.T("MsgCouldNotApply"));
        }

        string args = $"--apply-global \"{colorId}\"{(restartExplorer ? " --restart" : "")}";
        var (ok, exitCode, cancelled, error) = Elevation.RunElevated(args, TimeSpan.FromMinutes(3));

        if (cancelled)
            return new ApplyOutcome(false, Loc.T("MsgUacCancelled"), true);
        if (!ok)
            return new ApplyOutcome(false, error ?? Loc.T("MsgElevateFailed"));

        return new ApplyOutcome(exitCode == 0,
            exitCode == 0
                ? Loc.F("MsgSystemApplied", color.Name)
                : Loc.T("MsgWindowsError"));
    }
}

/// <summary>Launches a copy of FolderHue with the Windows UAC prompt.</summary>
public static class Elevation
{
    public static (bool Ok, int ExitCode, bool Cancelled, string? Error) RunElevated(string arguments, TimeSpan timeout)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.ExePath,
                Arguments = arguments,
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return (false, -1, false, "No se pudo iniciar el proceso elevado.");

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch { }
                return (false, -1, false, Loc.T("MsgTimeout"));
            }
            return (true, process.ExitCode, false, null);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Write("UAC cancelado por el usuario.");
            return (false, -1, true, null);
        }
        catch (Exception ex)
        {
            Log.Write("No se pudo elevar el proceso", ex);
            return (false, -1, false, ex.Message);
        }
    }
}
