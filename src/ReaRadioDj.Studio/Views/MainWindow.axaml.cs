using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ReaRadioDj.Studio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
}
