using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class ClockView : UserControl
{
    public ClockView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        // the clock lies below the counters in the layout; it only shows when it has room to be read
        Clock.SizeChanged += (_, e) => Clock.Opacity = e.NewSize.Height >= 70 ? 1 : 0;
    }
}
