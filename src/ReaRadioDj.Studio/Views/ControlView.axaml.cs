using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class ControlView : UserControl
{
    public ControlView()
    {
        InitializeComponent();
        MediaDrag.Bind(this); // also attaches the window to the live state
    }
}
