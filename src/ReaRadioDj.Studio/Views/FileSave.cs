using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.Views;

/// <summary>Asks where to save and writes a file the engine produces (CSV exports, reports).</summary>
public static class FileSave
{
    public static async Task<bool> FromEngineAsync(Visual owner, string title, string suggestedName, string url)
    {
        if (TopLevel.GetTopLevel(owner)?.StorageProvider is not { } storage) return false;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
        });
        if (file is null) return false;
        var bytes = await StudioHub.Current.Client.GetBytesAsync(url);
        if (bytes is null) return false;
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(bytes);
        StudioHub.Current.Status = $"opgeslagen: {file.Name}";
        return true;
    }
}
