using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class VoiceView : UserControl
{
    public VoiceView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
