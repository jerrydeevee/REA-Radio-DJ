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
        // The size setting (Instellingen › Uiterlijk) scales everything in the window.
        void Rescale() => Zoom.LayoutTransform = new Avalonia.Media.ScaleTransform(Services.Appearance.Scale, Services.Appearance.Scale);
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
        }
        SizeChanged += (_, _) => Arrange();
        Arrange();
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
