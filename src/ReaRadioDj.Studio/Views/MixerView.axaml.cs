using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class MixerView : UserControl
{
    public MixerView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
