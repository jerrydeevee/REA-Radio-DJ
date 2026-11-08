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
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm };
            window.Closing += (_, _) => vm.SaveOnExit();
            desktop.MainWindow = window;
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
