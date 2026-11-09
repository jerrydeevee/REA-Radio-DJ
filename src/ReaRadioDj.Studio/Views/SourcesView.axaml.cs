using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class SourcesView : UserControl
{
    public SourcesView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
