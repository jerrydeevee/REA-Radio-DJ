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
        // Four columns as designed; a narrower wall gets fewer columns (tiles stay at least about 120 px wide) and scrolls.
        void Fit(double width)
        {
            if (Grid.ItemsPanelRoot is Avalonia.Controls.Primitives.UniformGrid panel) panel.Columns = Columns(width);
        }
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
        Grid.Loaded += (_, _) => Fit(Bounds.Width);
    }

    /// <summary>Columns for a wall of this width: 4 from 560 px, 3 from 400, 2 from 250, else 1.</summary>
    public static int Columns(double width) => width <= 0 || width >= 560 ? 4 : width >= 400 ? 3 : width >= 250 ? 2 : 1;
}
