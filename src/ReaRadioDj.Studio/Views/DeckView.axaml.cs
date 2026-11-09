using Avalonia.Controls;

namespace ReaRadioDj.Studio.Views;

public partial class DeckView : UserControl
{
    public DeckView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
    }
}
