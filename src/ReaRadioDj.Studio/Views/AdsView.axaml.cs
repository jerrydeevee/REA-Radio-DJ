using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class AdsView : UserControl
{
    public AdsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        Export.Click += async (_, _) =>
        {
            if (DataContext is AdsTool t) await FileSave.FromEngineAsync(this, "Uitzendbewijs opslaan", t.ProofFileName, t.ProofUrl);
        };
        ExportReport.Click += async (_, _) =>
        {
            if (DataContext is AdsTool t) await FileSave.FromEngineAsync(this, "Reclamerapport opslaan", t.ReportFileName, t.ReportUrl);
        };
        ExportJson.Click += async (_, _) =>
        {
            if (DataContext is AdsTool t) await FileSave.FromEngineAsync(this, "Uitzendbewijs als JSON opslaan", t.JsonFileName, t.JsonUrl);
        };
    }
}
