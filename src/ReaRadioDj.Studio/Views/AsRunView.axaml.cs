using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class AsRunView : UserControl
{
    public AsRunView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        Export.Click += async (_, _) =>
        {
            if (DataContext is not AsRunTool tool || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "As-run exporteren",
                SuggestedFileName = tool.SuggestedFileName,
                DefaultExtension = "csv",
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync();
            await tool.ExportAsync(stream);
        };
        Report.Click += async (_, _) =>
        {
            if (DataContext is AsRunTool t) await FileSave.FromEngineAsync(this, "Muziekrapport opslaan", t.ReportFileName(false), t.ReportUrl(false));
        };
        ReportPdf.Click += async (_, _) =>
        {
            if (DataContext is AsRunTool t) await FileSave.FromEngineAsync(this, "Muziekrapport als PDF opslaan", t.ReportFileName(false, "pdf"), t.ReportUrl(false, "pdf"));
        };
        ReportSumPdf.Click += async (_, _) =>
        {
            if (DataContext is AsRunTool t) await FileSave.FromEngineAsync(this, "Overzicht als PDF opslaan", t.ReportFileName(true, "pdf"), t.ReportUrl(true, "pdf"));
        };
        ReportSum.Click += async (_, _) =>
        {
            if (DataContext is AsRunTool t) await FileSave.FromEngineAsync(this, "Overzicht per nummer opslaan", t.ReportFileName(true), t.ReportUrl(true));
        };
    }
}
