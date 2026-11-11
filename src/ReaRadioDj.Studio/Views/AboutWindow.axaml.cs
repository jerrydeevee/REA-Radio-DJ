using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ReaRadioDj.Studio.Views;

/// <summary>
/// Who made REA-Radio-DJ: shown as a splash while the studio starts and under Info › Over REA-Radio-DJ.
/// </summary>
public partial class AboutWindow : Window
{
    public const string Maker = "ICT Holland";
    public const string Company = "DVC-it";
    public const string SourceUrl = "https://github.com/jerrydeevee/REA-Radio-DJ";

    /// <summary>The program version, e.g. "0.1.0".</summary>
    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private readonly bool _splash;
    private readonly DateTime _shownAt = DateTime.UtcNow;

    public AboutWindow() : this(false) { }

    private AboutWindow(bool splash)
    {
        _splash = splash;
        InitializeComponent();
        VersionText.Text = $"Studio · versie {Version}";
        CopyrightText.Text = $"© {Math.Max(2026, DateTime.Now.Year)} {Maker} ({Company})";
        Loading.IsVisible = splash;
        Details.IsVisible = !splash;
        SourceButton.Click += (_, _) => Open(SourceUrl);
        LicenseButton.Click += (_, _) => Open(SourceUrl + "/blob/main/NOTICE.md");
        CloseButton.Click += (_, _) => Close();
        if (splash)
        {
            // A splash has no frame or taskbar button and closes on a click.
            SystemDecorations = SystemDecorations.None;
            ShowInTaskbar = false;
            Topmost = true;
            PointerPressed += (_, _) => Close();
        }

        // --about-shot <file.png>: renders the Info window to an image and closes (documentation, tests).
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, splash ? "--splash-shot" : "--about-shot");
        if (i >= 0 && i + 1 < args.Length)
        {
            string file = args[i + 1];
            Opened += (_, _) => DispatcherTimer.RunOnce(() =>
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
                bitmap.Render(this);
                bitmap.Save(file);
            }, TimeSpan.FromSeconds(0.6));
        }
    }

    /// <summary>The start-up splash, or null when it is switched off (--no-splash, or a screenshot run).</summary>
    public static AboutWindow? Splash()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--no-splash") || args.Contains("--screenshot")) return null;
        return new AboutWindow(true);
    }

    /// <summary>Closes the splash once the studio is open, but not before it was readable (2 seconds).</summary>
    public void CloseWhenOpen(Window main)
    {
        void Done()
        {
            var wait = TimeSpan.FromSeconds(2) - (DateTime.UtcNow - _shownAt);
            DispatcherTimer.RunOnce(() => { Close(); main.Activate(); }, wait > TimeSpan.FromSeconds(0.3) ? wait : TimeSpan.FromSeconds(0.3));
        }
        if (main.IsVisible) Done(); else main.Opened += (_, _) => Done();
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
