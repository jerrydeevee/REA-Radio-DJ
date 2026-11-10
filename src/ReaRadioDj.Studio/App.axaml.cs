using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ReaRadioDj.Studio.ViewModels;
using ReaRadioDj.Studio.Views;

namespace ReaRadioDj.Studio;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // One faulty control must not close the studio during a broadcast: log the error and keep running.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            LogError(e.Exception);
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Workbenches are windows of their own; closing the main window still closes the whole studio.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            MainWindow Build()
            {
                var vm = new MainWindowViewModel();
                var window = new MainWindow { DataContext = vm };
                window.Closing += (_, _) => vm.SaveOnExit();
                desktop.MainWindow = window;
                // Spotify Soloist (developer test): started with the studio when set, and always stopped with it.
                var soloist = Services.StudioHub.Current.Settings.Soloist;
                if (soloist.StartWithStudio && Services.SoloistLauncher.Start(soloist) is { } problem)
                    Services.StudioHub.Current.Status = "Soloist: " + problem;
                desktop.Exit += (_, _) => Services.SoloistLauncher.Stop();
                return window;
            }

            // The splash (who made the program) shows first; the studio is built once it is on screen.
            if (AboutWindow.Splash() is { } splash)
            {
                splash.Show();
                DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        var window = Build();
                        window.Show();
                        splash.CloseWhenOpen(window);
                    }
                    catch
                    {
                        splash.Close();
                        throw;
                    }
                }, TimeSpan.FromMilliseconds(80));
            }
            else Build();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void LogError(Exception ex)
    {
        var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}";
        Console.Error.Write(text);
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REA-Radio-DJ");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "studio-errors.log"), text);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
