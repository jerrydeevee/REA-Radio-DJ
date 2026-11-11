using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class AirStatsView : UserControl
{
    public AirStatsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        // Two lists side by side when there is room, else one under the other.
        SizeChanged += (_, e) => Lists.Columns = e.NewSize.Width >= 720 ? 2 : 1;
        Export.Click += async (_, _) =>
        {
            if (DataContext is AirStatsTool t) await FileSave.FromEngineAsync(this, "Uitzendstatistiek opslaan", t.CsvFileName, t.CsvUrl);
        };
    }
}
