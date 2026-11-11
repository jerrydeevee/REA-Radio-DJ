using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class PflView : UserControl
{
    public PflView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
