using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class DjMidiView : UserControl
{
    public DjMidiView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
