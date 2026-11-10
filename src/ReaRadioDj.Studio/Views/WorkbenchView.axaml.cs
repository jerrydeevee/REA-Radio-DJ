using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class WorkbenchView : UserControl
{
    public WorkbenchView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
