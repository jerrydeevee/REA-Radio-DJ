using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class StreamView : UserControl
{
    public StreamView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
