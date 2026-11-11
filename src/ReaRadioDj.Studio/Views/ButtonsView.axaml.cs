using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class ButtonsView : UserControl
{
    public ButtonsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
