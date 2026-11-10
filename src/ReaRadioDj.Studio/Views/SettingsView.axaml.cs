using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        AttachedToVisualTree += (_, _) =>
        {
            if (StartTab is null) return;
            Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().FirstOrDefault(t => (t.Header as string) == StartTab);
            StartTab = null;
        };
    }

    /// <summary>Tab to open the next time the window appears (from the command line).</summary>
    public static string? StartTab { get; set; }

    private async void OnBrowseSoloist(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsTool vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Waar staat soloist.exe?",
            FileTypeFilter = [new FilePickerFileType("Spotify Soloist") { Patterns = ["soloist.exe"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) vm.SoloistPath = path;
    }

    private static readonly FilePickerFileType BackupType = new("Reservekopie REA-Radio-DJ") { Patterns = ["*.rea-backup"] };

    private async void OnBackup(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsTool vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Reservekopie opslaan",
            SuggestedFileName = $"{vm.StationName}-{DateTime.Now:yyyyMMdd-HHmm}.rea-backup",
            DefaultExtension = "rea-backup",
            FileTypeChoices = [BackupType],
        });
        if (file is null) return;
        var data = await vm.FetchBackupAsync();
        if (data is null) return;
        await using (var s = await file.OpenWriteAsync()) await s.WriteAsync(data);
        vm.BackupStatus = $"Reservekopie opgeslagen: {file.Name} ({data.Length / 1024} kB).";
    }

    private async void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsTool vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Reservekopie terugzetten (vervangt alle instellingen van de engine)",
            FileTypeFilter = [BackupType],
        });
        if (files.Count == 0) return;
        await using var s = await files[0].OpenReadAsync();
        var buffer = new MemoryStream();
        await s.CopyToAsync(buffer);
        await vm.RestoreBackupAsync(buffer.ToArray());
    }
}
