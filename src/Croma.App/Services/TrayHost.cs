using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FolderHue.Core;
using Application = System.Windows.Application;
using Color = System.Drawing.Color;
using Font = System.Drawing.Font;

namespace FolderHue.App.Services;

/// <summary>The always-running tray presence: quick colour menu, options and exit.</summary>
public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu;
    private Icon? _trayIcon;
    private readonly System.Windows.Forms.Timer _balloonTimer;

    public event EventHandler? OpenRequested;
    public bool ExitRequested { get; private set; }

    public TrayHost()
    {
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable()),
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(4)
        };
        _menu.Opening += (_, _) => RebuildMenu();

        _notify = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = _menu,
            Text = Loc.T("TrayTip")
        };
        _notify.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        UpdateTrayIcon();

        _balloonTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _balloonTimer.Tick += (_, _) => _balloonTimer.Stop();

        ColorCoordinator.Instance.StatusChanged += OnStatusChanged;
        ColorCoordinator.Instance.GlobalChanged += _ => UpdateTrayIcon();
    }

    private void OnStatusChanged(string message)
    {
        if (!ConfigStore.Current.ShowNotifications) return;
        try
        {
            string text = message.Length > 110 ? message[..110] + "…" : message;
            _notify.ShowBalloonTip(2600, "FolderHue", text, ToolTipIcon.None);
        }
        catch { }
    }

    private void UpdateTrayIcon()
    {
        try
        {
            // The tray always shows Croma's own mark, never a folder.
            var icon = new Icon(IconStore.EnsureAppIcon(), 16, 16);
            _notify.Icon = icon;
            _trayIcon?.Dispose();
            _trayIcon = icon;
        }
        catch (Exception ex) { Log.Write("No se pudo actualizar el icono de la bandeja", ex); }
    }

    private static Image Swatch(FolderColor color, int size = 16)
    {
        using var bmp = IconBaker.Render(color, size);
        return new Bitmap(bmp);
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        var cfg = ConfigStore.Current;
        var current = Palette.Get(cfg.GlobalColor);

        var colors = new ToolStripMenuItem(Loc.T("FolderColor")) { Image = Swatch(current) };
        foreach (var group in PaletteGroups.Build())
        {
            var groupItem = new ToolStripMenuItem(Loc.T("Group" + group.Name));
            foreach (var color in group.Colors)
            {
                var item = new ToolStripMenuItem(color.Name, Swatch(color))
                {
                    Checked = color.Id == current.Id,
                    Tag = color
                };
                item.Click += async (_, _) => await ApplyGlobalAsync(color);
                groupItem.DropDownItems.Add(item);
            }
            colors.DropDownItems.Add(groupItem);
        }
        _menu.Items.Add(colors);

        if (cfg.Folders.Count > 0)
        {
            var folders = new ToolStripMenuItem(Loc.F("CustomFolders", cfg.FolderCount));
            foreach (var entry in cfg.Folders.Values.OrderBy(f => f.Path))
            {
                if (!Directory.Exists(entry.Path)) continue;
                var color = Palette.Get(entry.Color);
                var item = new ToolStripMenuItem(
                    $"{Path.GetFileName(entry.Path)}  ·  {Loc.ColorName(color)}", Swatch(color))
                {
                    ToolTipText = entry.Path
                };
                item.Click += (_, _) => OpenPicker(entry.Path);
                folders.DropDownItems.Add(item);
            }
            if (folders.DropDownItems.Count > 0) _menu.Items.Add(folders);
        }

        var applyTo = new ToolStripMenuItem(Loc.T("ApplyToFolder"));
        applyTo.Click += (_, _) => PickFolder();
        _menu.Items.Add(applyTo);

        _menu.Items.Add(new ToolStripSeparator());

        var open = new ToolStripMenuItem(Loc.T("OpenFolderHue")) { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(open);

        var options = new ToolStripMenuItem(Loc.T("Options"));
        options.DropDownItems.Add(Toggle(Loc.T("StartWithWindows"), cfg.StartWithWindows, v =>
        {
            cfg.StartWithWindows = v;
            StartupService.SetEnabled(v);
            ConfigStore.Save();
        }));
        options.DropDownItems.Add(Toggle(Loc.T("RestartExplorer"), cfg.RestartExplorer, v =>
        {
            cfg.RestartExplorer = v;
            ConfigStore.Save();
        }));
        options.DropDownItems.Add(Toggle(Loc.T("ApplyShortcuts"), cfg.ApplyToShortcuts, v =>
        {
            cfg.ApplyToShortcuts = v;
            ConfigStore.Save();
        }));
        options.DropDownItems.Add(Toggle(Loc.T("TrayNotifications"), cfg.ShowNotifications, v =>
        {
            cfg.ShowNotifications = v;
            ConfigStore.Save();
        }));
        _menu.Items.Add(options);

        _menu.Items.Add(new ToolStripSeparator());

        var restore = new ToolStripMenuItem(Loc.T("RestoreAll"));
        restore.Click += async (_, _) =>
        {
            var outcome = await ColorCoordinator.Instance.RestoreEverythingAsync();
            OnStatusChanged(outcome.Message);
        };
        _menu.Items.Add(restore);

        var exit = new ToolStripMenuItem(Loc.T("Exit"));
        exit.Click += (_, _) =>
        {
            ExitRequested = true;
            Application.Current.Dispatcher.Invoke(() => App.Current.RequestExit());
        };
        _menu.Items.Add(exit);
    }

    private static ToolStripMenuItem Toggle(string text, bool value, Action<bool> onChange)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = value };
        item.Click += (_, _) => onChange(item.Checked);
        return item;
    }

    private async Task ApplyGlobalAsync(FolderColor color)
    {
        var outcome = await ColorCoordinator.Instance.ApplyGlobalAsync(color);
        if (!outcome.Success) OnStatusChanged(outcome.Message);
    }

    private void PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("ChooseFolderToRecolor"),
            Multiselect = false
        };
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.FolderName))
            OpenPicker(dialog.FolderName);
    }

    private void OpenPicker(string folder)
    {
        if (!Directory.Exists(folder)) return;
        var popup = new FolderPickerWindow(folder, ownsApplication: false);
        popup.Show();
        popup.Activate();
    }

    public void Dispose()
    {
        try
        {
            _notify.Visible = false;
            _notify.Dispose();
            _menu.Dispose();
            _trayIcon?.Dispose();
            _balloonTimer.Dispose();
        }
        catch { }
    }

    /// <summary>Dark colours for the native context menu, matching the app.</summary>
    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static Color Bg => Color.FromArgb(20, 22, 28);
        private static Color Hover => Color.FromArgb(38, 42, 54);
        private static Color Border => Color.FromArgb(44, 48, 60);

        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Bg;
        public override Color MenuItemPressedGradientEnd => Bg;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
    }
}
