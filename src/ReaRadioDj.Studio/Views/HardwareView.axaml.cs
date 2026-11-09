using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class HardwareView : UserControl
{
    public HardwareView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
