using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class CartwallView : UserControl
{
    public CartwallView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        // Drop a library item on a button to assign it.
        MediaDrag.AcceptDrops(Grid, (id, e) =>
            DataContext is CartwallTool wall && MediaDrag.ItemAt<CartButton>(Grid, e) is { } b ? wall.Assign(b, id) : Task.CompletedTask);
    }
}
