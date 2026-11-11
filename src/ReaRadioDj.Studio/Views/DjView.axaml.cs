using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class DjView : UserControl
{
    public DjView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
