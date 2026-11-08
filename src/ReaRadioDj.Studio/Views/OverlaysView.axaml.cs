using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class OverlaysView : UserControl
{
    public OverlaysView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
