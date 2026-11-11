using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class VoiceTrackView : UserControl
{
    public VoiceTrackView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
