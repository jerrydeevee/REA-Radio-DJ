using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class ItemView : UserControl
{
    public ItemView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
