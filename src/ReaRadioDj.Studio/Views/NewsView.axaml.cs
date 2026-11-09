using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class NewsView : UserControl
{
    public NewsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
