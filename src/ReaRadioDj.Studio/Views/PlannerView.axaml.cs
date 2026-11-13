using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class PlannerView : UserControl
{
    public static readonly IReadOnlyList<string> Hours = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();

    public PlannerView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        // "--show planner:Klokken": the clocks arrive a moment after the window opens
        AttachedToVisualTree += (_, _) => Avalonia.Threading.DispatcherTimer.RunOnce(
            () => (DataContext as PlannerTool)?.ApplyStartTab(), TimeSpan.FromSeconds(2));
    }

    /// <summary>Picks a playlist file for a clock position. The path must be one the engine can read (same pc or a share).</summary>
    private async void OnPickPlaylist(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not RuleRow row || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Afspeellijst voor deze plek in de klok",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Afspeellijsten") { Patterns = ["*.m3u", "*.m3u8", "*.pls", "*.xspf"] }, FilePickerFileTypes.All],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        row.Source = path;
        if (DataContext is PlannerTool tool) await tool.CheckPlaylistCommand.ExecuteAsync(row);
    }
}
