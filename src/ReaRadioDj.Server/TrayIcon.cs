using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Server;

/// <summary>
/// The engine's icon in the Windows notification area (next to the clock). The engine has no window of its own, so
/// this is where you find it back: double-click opens the studio, the menu shows what is on air and can stop the
/// engine. The icon turns grey when nothing is playing. Not shown when the engine runs without a desktop (as a
/// service) or with --no-tray.
/// </summary>
public sealed class TrayIcon(PlayoutEngine engine, IHostApplicationLifetime lifetime, IServer server, ILogger<TrayIcon> log) : IHostedService
{
    private const string StudioExe = "REA-Radio-DJ-Studio.exe";
    private Thread? _thread;
    private volatile bool _stopping;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "REA-Radio-DJ tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
        return Task.CompletedTask;
    }

    private void Run()
    {
        try
        {
            Application.EnableVisualStyles();
            var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
            var size = SystemInformation.SmallIconSize;
            using var live = new Icon(Path.Combine(assets, "rea.ico"), size);
            using var idle = new Icon(Path.Combine(assets, "rea-idle.ico"), size);

            using var menu = new ContextMenuStrip();
            var status = new ToolStripMenuItem("REA-Radio-DJ") { Enabled = false };
            var openStudio = new ToolStripMenuItem("Studio openen", null, (_, _) => OpenStudio()) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) };
            var openWeb = new ToolStripMenuItem("Webinterface openen", null, (_, _) => Open(EngineUrl()));
            var next = new ToolStripMenuItem("Volgende starten", null, (_, _) => engine.Next());
            var auto = new ToolStripMenuItem("Automatisch", null, (_, _) => engine.SetAutoPlay(!engine.AutoPlay));
            var quit = new ToolStripMenuItem("Engine stoppen…", null, (_, _) => ConfirmStop());
            menu.Items.AddRange([status, new ToolStripSeparator(), openStudio, openWeb, new ToolStripSeparator(), next, auto,
                new ToolStripSeparator(), quit]);

            using var icon = new NotifyIcon { Icon = live, Text = "REA-Radio-DJ", ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += (_, _) => OpenStudio();
            icon.ShowBalloonTip(4000, "REA-Radio-DJ draait", "De uitzending loopt ook zonder studio. Dubbelklik dit icoon om de studio te openen.", ToolTipIcon.Info);

            using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, _) =>
            {
                if (_stopping)
                {
                    timer.Stop();
                    icon.Visible = false;
                    Application.ExitThread();
                    return;
                }
                var state = engine.GetState();
                var now = state.Current;
                string line = now is null ? (state.AutoPlay ? "Wacht op muziek" : "Stil (live-assist)") : $"ON AIR: {now.Label}";
                status.Text = line.Length > 60 ? line[..57] + "…" : line;
                auto.Checked = state.AutoPlay;
                icon.Icon = now is null ? idle : live;
                var tip = $"REA-Radio-DJ · {(state.AutoPlay ? "automatisch" : "live-assist")}\n{line}";
                icon.Text = tip.Length > 127 ? tip[..126] : tip;
            };
            timer.Start();
            Application.Run();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Tray icon not available");
        }
    }

    private void ConfirmStop()
    {
        var answer = MessageBox.Show("Als je de engine stopt, stopt ook de uitzending.\n\nEngine stoppen?", "REA-Radio-DJ",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes) lifetime.StopApplication();
    }

    private string EngineUrl()
    {
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? "http://localhost:8600";
        // A wildcard binding is not a URL a browser or the studio can open.
        return address.Replace("://+", "://localhost").Replace("://*", "://localhost").Replace("://0.0.0.0", "://localhost");
    }

    private void OpenStudio()
    {
        // Already running: bring it to the front.
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(StudioExe)))
        {
            if (p.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
            SetForegroundWindow(p.MainWindowHandle);
            return;
        }
        var exe = FindStudio();
        if (exe is null)
        {
            MessageBox.Show($"{StudioExe} is niet gevonden naast de engine.", "REA-Radio-DJ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(exe, $"--engine {EngineUrl()} --no-engine") { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
    }

    private static string? FindStudio()
    {
        var here = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var candidates = new List<string>
        {
            Path.Combine(here.FullName, StudioExe),
            Path.Combine(here.FullName, "..", "studio", StudioExe),
        };
        // Development layout: src/ReaRadioDj.Server/bin/<cfg>/<tfm>/ -> src/ReaRadioDj.Studio/bin/<cfg>/<tfm>/
        if (here.Parent?.Parent?.Parent?.Parent is { } src)
            candidates.Add(Path.Combine(src.FullName, "ReaRadioDj.Studio", "bin", here.Parent.Name, here.Name, StudioExe));
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { }
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
}
