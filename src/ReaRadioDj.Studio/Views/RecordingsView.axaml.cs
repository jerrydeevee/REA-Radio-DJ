using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class RecordingsView : UserControl
{
    public RecordingsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);   // attaches the window to the live state when it is shown
    }
}