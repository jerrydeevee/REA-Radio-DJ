using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class WishlistView : UserControl
{
    public WishlistView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        ExportMissing.Click += async (_, _) =>
            await FileSave.FromEngineAsync(this, "Ontbrekende nummers exporteren", "ontbrekende-nummers.csv", "/api/wishlist/missing.csv");
    }
}
