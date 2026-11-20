using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FolderHue.App.Services;
using FolderHue.Core;

namespace FolderHue.App;

/// <summary>
/// The visual colour picker that appears exactly where the Explorer context menu was:
/// a compact grid of folder swatches, one click to paint, no dialogs.
/// </summary>
public partial class FolderPickerWindow : Window
{
    private readonly string[] _folders;
    private readonly bool _ownsApplication;
    private readonly DispatcherTimer _closeTimer;
    private bool _armed;

    public FolderPickerWindow(string folder, bool ownsApplication = true)
        : this(new[] { folder }, ownsApplication) { }

    public FolderPickerWindow(string[] folders, bool ownsApplication = true)
    {
        InitializeComponent();

        _folders = folders.Where(Directory.Exists).ToArray();
        _ownsApplication = ownsApplication;

        HeaderIcon.Source = IconStore.AppIconImage();
        SubText.Text = _folders.Length switch
        {
            0 => Loc.T("NoFolder"),
            1 => Path.GetFileName(_folders[0].TrimEnd(Path.DirectorySeparatorChar)),
            _ => Loc.F("FoldersSelected", _folders.Length)
        };
        if (_folders.Length > 1) HeaderText.Text = Loc.T("FolderColors");
        Title = "FolderHue";

        BuildSwatches();

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };

        Loaded += OnLoaded;
        Deactivated += (_, _) => { if (_armed) Close(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var work = SystemParameters.WorkArea;

        double x = cursor.X - Root.ActualWidth + 12;
        double y = cursor.Y - Root.ActualHeight + 12;

        if (x + Root.ActualWidth > work.Right) x = work.Right - Root.ActualWidth - 8;
        if (y + Root.ActualHeight > work.Bottom) y = work.Bottom - Root.ActualHeight - 8;
        if (x < work.Left) x = work.Left + 8;
        if (y < work.Top) y = work.Top + 8;

        Left = x;
        Top = y;
        Activate();

        // Ignore the deactivation fired while the window is still settling.
        var arm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        arm.Tick += (_, _) => { arm.Stop(); _armed = true; };
        arm.Start();
    }

    private void BuildSwatches()
    {
        foreach (var color in PaletteGroups.Ordered())
        {
            var image = new Image
            {
                Source = IconStore.Preview(color, 36),
                Width = 36,
                Height = 36
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

            var button = new Button
            {
                Content = image,
                Style = (Style)FindResource("SwatchButton"),
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 3, 3),
                ToolTip = Loc.ColorName(color)
            };
            button.Click += async (_, _) => await PaintAsync(color);
            SwatchHost.Children.Add(button);
        }
    }

    private async Task PaintAsync(FolderColor color)
    {
        if (_folders.Length == 0) { Close(); return; }

        foreach (string folder in _folders)
            await ColorCoordinator.Instance.ApplyFolderAsync(folder, color);


        SubText.Text = _folders.Length == 1
            ? $"✓ {Loc.ColorName(color)}"
            : Loc.F("MsgAppliedMany", _folders.Length, Loc.ColorName(color));
        _closeTimer.Start();
    }

    private async void Original_Click(object sender, RoutedEventArgs e)
    {
        foreach (string folder in _folders)
            await Task.Run(() => FolderPainter.Restore(folder));

        SubText.Text = Loc.T("MsgDone");
        _closeTimer.Start();
    }

    private void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.ExePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) { Log.Write("Could not open the main window", ex); }
        Close();
    }
}
