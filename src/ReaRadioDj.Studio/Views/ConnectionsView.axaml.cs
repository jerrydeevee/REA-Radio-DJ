using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class ConnectionsView : UserControl
{
    public ConnectionsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
