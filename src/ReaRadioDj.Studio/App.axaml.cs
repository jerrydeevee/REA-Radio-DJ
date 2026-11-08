using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ReaRadioDj.Studio.ViewModels;
using ReaRadioDj.Studio.Views;

namespace ReaRadioDj.Studio;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            var window = new MainWindow { DataContext = vm };
            window.Closing += (_, _) => vm.SaveOnExit();
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
