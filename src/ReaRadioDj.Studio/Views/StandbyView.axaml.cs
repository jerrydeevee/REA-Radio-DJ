using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class StandbyView : UserControl
{
    public StandbyView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
