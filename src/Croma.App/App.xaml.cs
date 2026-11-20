using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using FolderHue.App.Services;
using FolderHue.Core;

namespace FolderHue.App;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private TrayHost? _tray;
    private MainWindow? _mainWindow;
    private bool _ownsInstance;

    /// <summary>Set by the tray so the window hides instead of closing the app.</summary>
    public static new App Current => (App)Application.Current;

    public TrayHost? Tray => _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Write("FolderHue iniciado. Args: " + string.Join(" ", e.Args));

        try
        {
            AppPaths.EnsureCreated();
            Services.Loc.Initialize(Services.ConfigStore.Current.Language);
            HandleArguments(e.Args);
        }
        catch (Exception ex)
        {
            Log.Write("Fallo fatal al arrancar", ex);
            if (e.Args.Length == 0 || !e.Args[0].StartsWith("--"))
                MessageBox.Show("FolderHue no pudo iniciarse:\n\n" + ex.Message,
                    "FolderHue", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void HandleArguments(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";

        switch (mode)
        {
            // --- transient, privileged helper invoked by ourselves through UAC ---
            case "--apply-global":
            {
                string colorId = args.Length > 1 ? args[1] : "original";
                bool restart = args.Contains("--restart", StringComparer.OrdinalIgnoreCase);
                int code = GlobalApplier.RunElevatedApply(colorId, restart);
                Shutdown(code);
                return;
            }

            // --- transient, unprivileged: paint one folder from the context menu ---
            case "--set-folder":
            {
                if (args.Length >= 3)
                {
                    var result = FolderPainter.Apply(args[1], args[2]);
                    Log.Write($"CLI --set-folder: {result.Message}");
                }
                Shutdown(0);
                return;
            }

            // --- transient: paint every selected folder with one colour ---
            // The shell appends each selected path after the colour id, so the order
            // is detected rather than assumed.
            case "--paint":
            {
                var rest = args.Skip(1).ToArray();
                string colorId = rest.FirstOrDefault(a => Palette.Exists(a)) ?? "original";
                var folders = rest
                    .Where(Directory.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                Log.Write($"CLI --paint {colorId}: {folders.Length} carpeta(s)");
                foreach (string folder in folders)
                {
                    try { FolderPainter.Apply(folder, colorId); }
                    catch (Exception ex) { Log.Write("No se pudo pintar " + folder, ex); }
                }
                if (folders.Length > 0 && ConfigStore.Current.RestartExplorer)
                    ShellNative.RestartExplorerWithCacheClear(TimeSpan.FromSeconds(1));
                Shutdown(0);
                return;
            }

            // --- transient, unprivileged: the visual picker opened from the menu ---
            case "--pick":
            {
                // A multi-selection arrives as several paths after the switch.
                var folders = args.Skip(1).Where(a => !a.StartsWith("--")).ToArray();
                folders = folders.Where(Directory.Exists).ToArray();
                if (folders.Length == 0)
                {
                    Shutdown(2);
                    return;
                }
                ShowPicker(folders);
                return;
            }

            case "--install":
            {
                Installer.RunFirstTimeSetup(silent: false);
                Shutdown(0);
                return;
            }

            case "--uninstall":
            {
                Installer.Uninstall();
                Shutdown(0);
                return;
            }
        }

        // --- long-running tray application ---
        StartTrayApplication(args.Contains("--tray", StringComparer.OrdinalIgnoreCase));
    }

    private void ShowPicker(string[] folders)
    {
        var popup = new FolderPickerWindow(folders);
        popup.Closed += (_, _) => Shutdown(0);
        popup.Show();
        popup.Activate();
    }

    private void StartTrayApplication(bool startHidden)
    {
        _instanceMutex = new Mutex(true, @"Local\FolderHue.SingleInstance", out _ownsInstance);
        if (!_ownsInstance)
        {
            // Another copy is already living in the tray: ask it to show itself.
            try
            {
                if (EventWaitHandle.TryOpenExisting(@"Local\FolderHue.ShowWindow", out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { }
            Shutdown(0);
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FolderHue.ShowWindow");
        var listener = new Thread(WaitForShowRequests) { IsBackground = true, Name = "FolderHue-SingleInstance" };
        listener.Start();

        Installer.RunFirstTimeSetup(silent: true);

        _tray = new TrayHost();
        _tray.OpenRequested += (_, _) => ShowMainWindow();

        if (!startHidden) ShowMainWindow();
    }

    private void WaitForShowRequests()
    {
        while (true)
        {
            try
            {
                _showEvent!.WaitOne();
                Dispatcher.Invoke(ShowMainWindow);
            }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex) { Log.Write("Error en el canal de instancia única", ex); }
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    /// <summary>Rebuilds every localised surface after a language change.</summary>
    public void Relocalize()
    {
        ContextMenuInstaller.Install();
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
            _mainWindow = null;
        }
        if (_tray is not null) ShowMainWindow();
    }

    public void RequestExit()
    {
        Log.Write("FolderHue cerrado por el usuario.");
        if (_mainWindow is not null) _mainWindow.AllowClose = true;
        try { _tray?.Dispose(); } catch { }
        try { _showEvent?.Dispose(); } catch { }
        try
        {
            if (_ownsInstance) _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
        catch { }
        Shutdown(0);
    }

    public void UpdateAccent(FolderColor color)
    {
        if (Resources["AccentBrush"] is SolidColorBrush brush && !brush.IsFrozen)
        {
            var c = color.IsOriginal ? Palette.Get("gold").Swatch : color.Swatch;
            brush.Color = Color.FromRgb(
                (byte)Math.Min(255, c.R + 40),
                (byte)Math.Min(255, c.G + 40),
                (byte)Math.Min(255, c.B + 40));
        }
    }
}
