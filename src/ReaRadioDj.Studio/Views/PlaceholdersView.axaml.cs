using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReaRadioDj.Contracts.Localization;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class PlaceholdersView : UserControl
{
    private static readonly FilePickerFileType Audio = new(Tr.T("Geluid"))
    {
        Patterns = ["*.mp3", "*.wav", "*.flac", "*.ogg", "*.m4a", "*.aac", "*.aif", "*.aiff"],
    };

    public PlaceholdersView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);   // attaches the window to the live state when it is shown
    }

    private async void OnDeliver(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PlaceholderVm p || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Tr.F("Geluid voor {0}", p.Name),
            FileTypeFilter = [Audio],
        });
        if (files.Count == 0) return;
        await using var s = await files[0].OpenReadAsync();
        var buffer = new MemoryStream();
        await s.CopyToAsync(buffer);
        await p.Owner.Deliver(p, files[0].Name, buffer.ToArray());
    }
}
