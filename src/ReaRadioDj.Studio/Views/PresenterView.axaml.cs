using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class PresenterView : UserControl
{
    public PresenterView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
