using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class ControlRoomView : UserControl
{
    public ControlRoomView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
