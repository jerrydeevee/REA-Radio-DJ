using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class MessagesView : UserControl
{
    public MessagesView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
