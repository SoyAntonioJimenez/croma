using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FolderHue.App.Services;
using FolderHue.Core;
using Microsoft.Win32;

namespace FolderHue.App;

public partial class MainWindow : Window
{
    private FolderColor _selected = Palette.Original;
    private readonly List<(Button Button, FolderColor Color)> _swatches = new();
    private bool _busy;

    /// <summary>Set when the app is really quitting, so the window may close.</summary>
    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        VersionText.Text = "v" + (typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "1.0.0");

        var logo = IconStore.AppIconImage();
        LogoImage.Source = logo;
        Icon = logo;

        ColorCoordinator.Instance.StatusChanged += OnStatusChanged;

        LoadOptions();
        LangEn.IsChecked = !Loc.IsSpanish;
        LangEs.IsChecked = Loc.IsSpanish;
        BuildPalette();
        SelectColor(Palette.Get(ConfigStore.Current.GlobalColor));
        UpdateWindowIcon(Palette.Get(ConfigStore.Current.GlobalColor));
        ColorCoordinator.Instance.GlobalChanged += color => Dispatcher.Invoke(() => UpdateWindowIcon(color));

        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    /// <summary>
    /// The taskbar button and Alt-Tab use the window icon, so keeping it in sync with
    /// the active colour is what makes the taskbar show the chosen colour.
    /// </summary>
    private void UpdateWindowIcon(FolderColor color)
    {
        try
        {
            Icon = color.IsOriginal ? IconStore.AppIconImage() : IconStore.Preview(color, 64);
        }
        catch (Exception ex) { Log.Write("No se pudo actualizar el icono de la ventana", ex); }
    }

    private void LoadOptions()
    {
        var cfg = ConfigStore.Current;
        StartupToggle.IsChecked = cfg.StartWithWindows;
        RestartToggle.IsChecked = cfg.RestartExplorer;
        ShortcutToggle.IsChecked = cfg.ApplyToShortcuts;
    }

    private void OnStatusChanged(string message) => Dispatcher.Invoke(() =>
    {
        StatusText.Text = message;
    });

    private void Status(string message) => StatusText.Text = message;

    // ── Palette ───────────────────────────────────────────────────────────

    private void BuildPalette()
    {
        PaletteHost.Children.Clear();
        _swatches.Clear();

        foreach (var color in PaletteGroups.Ordered())
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Width = 66 };

            var image = new Image
            {
                Source = IconStore.Preview(color, 44),
                Width = 44,
                Height = 44,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var label = new TextBlock
            {
                Text = Loc.ColorName(color),
                FontSize = 10.5,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)FindResource("MutedBrush")
            };

            stack.Children.Add(image);
            stack.Children.Add(label);

            var button = new Button
            {
                Content = stack,
                Style = (Style)FindResource("SwatchButton"),
                ToolTip = $"{Loc.ColorName(color)}  ·  {color.Hex}",
                Margin = new Thickness(1)
            };

            button.Click += (_, _) => SelectColor(color);
            PaletteHost.Children.Add(button);
            _swatches.Add((button, color));
            button.Tag = color;
        }

        PaletteCountText.Text = Loc.F("ColorsCount", Palette.All.Count);
    }

    private void SelectColor(FolderColor color)
    {
        _selected = color;

        PreviewImage.Source = IconStore.Preview(color, 118);

        ColorNameText.Text = color.IsOriginal ? Loc.T("OriginalYellow") : Loc.ColorName(color);
        ColorHexText.Text = color.IsOriginal
            ? Loc.T("OriginalDesc")
            : $"{Loc.ColorName(color)}  ·  {color.Hex}";

        App.Current.UpdateAccent(color);

        foreach (var (swatch, swatchColor) in _swatches)
            swatch.Tag = swatchColor.Id == color.Id ? "selected" : null;
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private async void ApplyGlobal_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var outcome = await ColorCoordinator.Instance.ApplyGlobalAsync(_selected);
            Status(outcome.Message);
                if (_selected.IsOriginal) BuildPalette();
        }
        finally { SetBusy(false); }
    }

    private async void ApplyFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dialog = new OpenFolderDialog
        {
            Title = Loc.T("ChooseFolders"),
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        var folders = dialog.FolderNames.ToList();
        if (folders.Count == 0) return;

        SetBusy(true);
        try
        {
            foreach (string folder in folders)
                await ColorCoordinator.Instance.ApplyFolderAsync(folder, _selected);

                Status(folders.Count == 1
                ? Loc.F("MsgFolderPainted", Path.GetFileName(folders[0]), Loc.ColorName(_selected))
                : Loc.F("MsgFoldersPainted", folders.Count, Loc.ColorName(_selected)));
        }
        finally { SetBusy(false); }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var confirm = MessageBox.Show(this,
            Loc.T("RestoreConfirm"),
            "FolderHue", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        SetBusy(true);
        try
        {
            var outcome = await ColorCoordinator.Instance.RestoreEverythingAsync();
            Status(outcome.Message);
                SelectColor(Palette.Original);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ApplyGlobalButton.IsEnabled = !busy;
        ApplyFolderButton.IsEnabled = !busy;
        RestoreButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    // ── Drag & drop ───────────────────────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var folders = paths.Where(Directory.Exists).ToList();
        if (folders.Count == 0) return;

        SetBusy(true);
        try
        {
            foreach (string folder in folders)
                await ColorCoordinator.Instance.ApplyFolderAsync(folder, _selected);

                Status(Loc.F("MsgFoldersPainted", folders.Count, Loc.ColorName(_selected)));
        }
        finally { SetBusy(false); }
    }

    // ── Options ───────────────────────────────────────────────────────────

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool value = StartupToggle.IsChecked == true;
        ConfigStore.Current.StartWithWindows = value;
        StartupService.SetEnabled(value);
        ConfigStore.Save();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        var cfg = ConfigStore.Current;
        cfg.RestartExplorer = RestartToggle.IsChecked == true;
        cfg.ApplyToShortcuts = ShortcutToggle.IsChecked == true;
        ConfigStore.Save();
    }

    // ── Window chrome ─────────────────────────────────────────────────────

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        string language = ReferenceEquals(sender, LangEs) ? "es" : "en";
        if (language == Loc.Language)
        {
            LangEn.IsChecked = !Loc.IsSpanish;
            LangEs.IsChecked = Loc.IsSpanish;
            return;
        }
        Loc.Set(language);
        App.Current.Relocalize();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
