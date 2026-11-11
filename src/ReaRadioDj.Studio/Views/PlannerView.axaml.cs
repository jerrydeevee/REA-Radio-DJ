using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class PlannerView : UserControl
{
    public static readonly IReadOnlyList<string> Hours = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();

    public PlannerView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
