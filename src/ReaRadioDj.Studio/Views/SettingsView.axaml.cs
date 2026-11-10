using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
