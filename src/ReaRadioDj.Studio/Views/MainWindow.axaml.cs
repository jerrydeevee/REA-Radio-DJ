using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ReaRadioDj.Studio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Hotkeys come from studio.json (Bediening › Sneltoetsen) and are rebuilt when they are saved.
        DataContextChanged += (_, _) => ApplyHotkeys();
        ViewModels.Hotkeys.Changed += ApplyHotkeys;
        Closed += (_, _) => { ViewModels.Hotkeys.Changed -= ApplyHotkeys; _global?.Dispose(); };
        Opened += (_, _) => ApplyHotkeys();
        // The size setting (Instellingen › Uiterlijk) scales the bars and overlays here; the windows in the docking
        // area scale their own contents (ScaledContent), so the docking area itself has no transform.
        void Rescale()
        {
            foreach (var zoom in new[] { Zoom, ZoomStatus, ZoomOnAir, ZoomOver })
                zoom.LayoutTransform = new Avalonia.Media.ScaleTransform(Services.Appearance.Scale, Services.Appearance.Scale);
        }
        Rescale();
        Services.Appearance.ScaleChanged += Rescale;
        Closed += (_, _) => Services.Appearance.ScaleChanged -= Rescale;
        // Workbenches need the screens: which one is where, and their size.
        Opened += (_, _) =>
        {
            if (DataContext is not ViewModels.MainWindowViewModel vm) return;
            vm.ScreenSource = () => Screens.All;
            vm.OnWindowOpened();
            Screens.Changed += (_, _) => vm.RefreshScreens();
        };
        // Narrower than 1900 px the transport buttons get their own row, so the title keeps its room.
        void Arrange()
        {
            bool wide = Bounds.Width >= 1900;
            Grid.SetRow(Transport, wide ? 0 : 1);
            Grid.SetColumn(Transport, wide ? 3 : 0);
            Grid.SetColumnSpan(Transport, wide ? 1 : 6);
            Transport.Margin = wide ? default : new Thickness(0, 10, 0, 0);
            // App bar: what drops off first is also elsewhere, so nothing falls off the right edge.
            double w = Bounds.Width / Services.Appearance.Scale;
            ConnectionText.IsVisible = w >= 1500;
            LayoutPick.IsVisible = w >= 1350;
            Brand.IsVisible = w >= 1200;
            bool twoRows = w < 1100;
            Grid.SetRow(Workspaces, twoRows ? 1 : 0);
            Grid.SetColumn(Workspaces, twoRows ? 0 : 2);
            Grid.SetColumnSpan(Workspaces, twoRows ? 6 : 1);
            Workspaces.Margin = twoRows ? new Thickness(0, 6, 0, 0) : new Thickness(12, 0, 0, 0);
        }
        SizeChanged += (_, _) => Arrange();
        Arrange();
        // Ctrl+K: go to a window by name (works wherever the focus is).
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (DataContext is not ViewModels.MainWindowViewModel vm) return;
            if (e.Key == Avalonia.Input.Key.K && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control)
            {
                vm.OpenGoToCommand.Execute(null);
                Dispatcher.UIThread.Post(() => GoToBox.Focus());
                e.Handled = true;
            }
            else if (vm.GoToOpen && e.Key == Avalonia.Input.Key.Escape) { vm.GoToOpen = false; e.Handled = true; }
            else if (e.Key == Avalonia.Input.Key.L && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control)
            {
                vm.Session.LockCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.Session.Open && e.Key == Avalonia.Input.Key.Escape) { vm.Session.CloseCommand.Execute(null); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        // The sign-in screen and the lock: the PIN box gets the focus, and the other studio windows (workbenches,
        // floating windows) wait as well.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ViewModels.MainWindowViewModel vm) return;
            vm.Session.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ViewModels.SessionVm.Open)) return;
                bool open = vm.Session.Open;
                if (open) Dispatcher.UIThread.Post(() => PinBox.Focus(), DispatcherPriority.Input);
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                    foreach (var w in life.Windows)
                        if (w != this) w.IsEnabled = !open;
            };
        };
        GoToBox.KeyDown += (_, e) =>
        {
            if (DataContext is not ViewModels.MainWindowViewModel vm) return;
            switch (e.Key)
            {
                case Avalonia.Input.Key.Enter: vm.GoToCommand.Execute(null); e.Handled = true; break;
                case Avalonia.Input.Key.Down when vm.GoToResults.Count > 1:
                    vm.GoToSelected = vm.GoToResults[1];
                    GoToList.Focus();
                    e.Handled = true;
                    break;
            }
        };
        GoToList.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && DataContext is ViewModels.MainWindowViewModel vm) { vm.GoToCommand.Execute(null); e.Handled = true; }
        };
        GoToList.Tapped += (_, _) => (DataContext as ViewModels.MainWindowViewModel)?.GoToCommand.Execute(null);
        // Stop interrupts the broadcast: the button only works when held for a second (the hotkey stops at once).
        var hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            StopButton.Classes.Remove("holding");
            if (DataContext is ViewModels.MainWindowViewModel vm) vm.StopCommand.Execute(null);
        };
        void Release()
        {
            if (!hold.IsEnabled) return;
            hold.Stop();
            StopButton.Classes.Remove("holding");
            if (DataContext is ViewModels.MainWindowViewModel vm) vm.Hub.Status = "Stop: houd de knop 1 seconde vast";
        }
        StopButton.AddHandler(PointerPressedEvent, (_, _) => { StopButton.Classes.Add("holding"); hold.Start(); }, RoutingStrategies.Tunnel);
        StopButton.AddHandler(PointerReleasedEvent, (_, _) => Release(), RoutingStrategies.Tunnel);
        StopButton.PointerCaptureLost += (_, _) => Release();
        // --screenshot <file.png> [seconds]: renders the window to an image and closes (documentation, tests).
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--about-shot")) Opened += (_, _) => _ = new AboutWindow().ShowDialog(this);
        // --window <width>x<height>: a fixed window size (screenshots of a whole panel).
        int wi = Array.IndexOf(args, "--window");
        if (wi >= 0 && wi + 1 < args.Length && args[wi + 1].Split('x') is [var ws, var hs] && int.TryParse(ws, out var ww) && int.TryParse(hs, out var wh))
        {
            WindowState = WindowState.Normal;
            Width = ww;
            Height = wh;
        }
        // --show <window id>: brings one window to the front after start (screenshots of a window that is a tab).
        int si = Array.IndexOf(args, "--show");
        if (si >= 0 && si + 1 < args.Length)
        {
            string id = args[si + 1];
            Opened += (_, _) => DispatcherTimer.RunOnce(() => (DataContext as ViewModels.MainWindowViewModel)?.ShowWindow(id), TimeSpan.FromSeconds(1));
        }
        int i = Array.IndexOf(args, "--screenshot");
        if (i >= 0 && i + 1 < args.Length)
        {
            string file = args[i + 1];
            double delay = i + 2 < args.Length && double.TryParse(args[i + 2], System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 6;
            Opened += (_, _) => DispatcherTimer.RunOnce(() =>
            {
                var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
                using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                bitmap.Render(this);
                bitmap.Save(file);
                Close();
            }, TimeSpan.FromSeconds(delay));
        }
    }

    // ---- Info menu
    private void OnAbout(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _ = new AboutWindow().ShowDialog(this);
    private void OnSource(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OpenUrl(AboutWindow.SourceUrl);
    private void OnLicense(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OpenUrl(AboutWindow.SourceUrl + "/blob/main/NOTICE.md");

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private Services.GlobalHotkeys? _global;

    private void ApplyHotkeys()
    {
        if (DataContext is not ViewModels.MainWindowViewModel vm) return;
        KeyBindings.Clear();
        foreach (var (gesture, command) in ViewModels.Hotkeys.Bindings(vm.Settings))
            KeyBindings.Add(new Avalonia.Input.KeyBinding { Gesture = gesture, Command = vm.RunEngineCommandCommand, CommandParameter = command });
        // Hotkeys marked "overal" also work while another program has the focus.
        _global ??= Services.GlobalHotkeys.For(this, c => Dispatcher.UIThread.Post(() => vm.RunEngineCommandCommand.Execute(c)));
        if (_global is null) return;
        var failed = _global.Apply(ViewModels.Hotkeys.GlobalBindings(vm.Settings));
        if (failed.Count > 0) vm.Hub.Status = $"Sneltoets niet overal te gebruiken (al in gebruik door een ander programma?): {string.Join(", ", failed)}";
    }
}
