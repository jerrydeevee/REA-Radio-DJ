using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class DjDeckView : UserControl
{
    public DjDeckView()
    {
        InitializeComponent();
        MediaDrag.AcceptDrops(this, (id, _) => DataContext is DjDeckVm deck ? deck.Load(id) : Task.CompletedTask);
        Wave.Clicked += seconds => { if (DataContext is DjDeckVm { Loaded: true } deck) _ = deck.SeekTo(seconds); };
    }
}
