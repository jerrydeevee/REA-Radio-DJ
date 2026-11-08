using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class ClockView : UserControl
{
    public ClockView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
