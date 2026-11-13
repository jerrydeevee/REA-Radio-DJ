using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class OutputView : UserControl
{
    public OutputView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
