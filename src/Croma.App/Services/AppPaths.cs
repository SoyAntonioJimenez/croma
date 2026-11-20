using System.IO;
using System.Text;

namespace FolderHue.App.Services;

/// <summary>Well-known locations used by FolderHue, created on demand.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderHue");

    public static string IconDir { get; } = Path.Combine(Root, "icons");
    public static string ConfigFile { get; } = Path.Combine(Root, "config.json");
    public static string BackupFile { get; } = Path.Combine(Root, "backup.json");
    public static string LogFile { get; } = Path.Combine(Root, "folderhue.log");

    /// <summary>Path of the running executable (single-file aware).</summary>
    public static string ExePath { get; } =
        Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;

    public static string AppIconPath { get; } = Path.Combine(IconDir, "app.ico");

    public static void EnsureCreated()
    {
        try { Directory.CreateDirectory(IconDir); } catch { }
    }
}

/// <summary>Rolling, size-capped log file so support cases leave a trail.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    public static void Write(string message, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureCreated();
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}";
                if (ex is not null) line += Environment.NewLine + "    " + ex;
                if (File.Exists(AppPaths.LogFile) && new FileInfo(AppPaths.LogFile).Length > MaxBytes)
                    File.Move(AppPaths.LogFile, AppPaths.LogFile + ".old", true);
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* logging must never break the app */ }
    }
}
