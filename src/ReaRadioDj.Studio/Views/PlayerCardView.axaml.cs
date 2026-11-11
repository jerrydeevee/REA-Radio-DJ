using Avalonia.Controls;
using Avalonia.Input;
using ReaRadioDj.Studio.Services;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class PlayerCardView : UserControl
{
    public PlayerCardView()
    {
        InitializeComponent();
        MediaDrag.AcceptDrops(this, (id, _) => DataContext is PlayerCard p ? p.Drop(id) : Task.CompletedTask);
        MediaDrag.DragSource(TitleText, dc => dc is PlayerCard { MediaId: long id } p ? (id, p.Title) : null, "player");
        Wave.Clicked += seconds => { if (DataContext is PlayerCard p && p.MediaId is not null) _ = p.SeekTo(seconds); };
        Bar.PointerPressed += (_, e) =>
        {
            if (DataContext is not PlayerCard p || p.MediaId is null) return;
            var player = p.Id == "pfl" ? StudioHub.Current.State?.Pfl : StudioHub.Current.State?.Players.FirstOrDefault(x => x.Id == p.Id);
            if (player is null) return;
            double fraction = Math.Clamp(e.GetPosition(Bar).X / Math.Max(1, Bar.Bounds.Width), 0, 1);
            _ = p.Seek(fraction, player.CueIn, player.CueOut);
            e.Handled = true;
        };
    }
}
