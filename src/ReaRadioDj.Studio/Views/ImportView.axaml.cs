using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Text;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        Pick.Click += async (_, _) =>
        {
            if (DataContext is not ImportTool t || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Logbestand van de muziekplanner",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Logbestanden") { Patterns = ["*.txt", "*.csv", "*.log", "*.lst", "*.tsv"] }, FilePickerFileTypes.All],
            });
            if (files.Count == 0) return;
            await using var stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            t.SetFile(files[0].Name, Decode(memory.ToArray()));
        };
        Export.Click += async (_, _) =>
            await FileSave.FromEngineAsync(this, "Bibliotheek exporteren", ImportTool.ExportFileName, ImportTool.ExportUrl);
    }

    /// <summary>Plain text of a file from a scheduler: UTF-8 when it is valid UTF-8, otherwise Windows Latin.</summary>
    private static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }
}
