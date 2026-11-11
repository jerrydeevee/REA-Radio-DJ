using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private async void OnBrowseMusic(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardVm vm && await PickAsync("Map met muziek") is { } path) vm.MusicFolder = path;
    }

    private async void OnBrowseJingles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardVm vm && await PickAsync("Map met jingles") is { } path) vm.JingleFolder = path;
    }

    private async Task<string?> PickAsync(string title)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return null;
        var picked = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
