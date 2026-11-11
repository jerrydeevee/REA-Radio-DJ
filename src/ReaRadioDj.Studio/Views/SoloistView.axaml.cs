using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class SoloistView : UserControl
{
    public SoloistView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
