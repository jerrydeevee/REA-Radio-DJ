using Avalonia;
using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class DeckView : UserControl
{
    public DeckView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        SizeChanged += (_, _) => Fit();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DeckTool t)
                t.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DeckTool.Columns)) Fit(); };
            Fit();
        };
    }

    /// <summary>Room per deck card decides what a card shows, so nothing is cut off or pushed over itself.</summary>
    private void Fit()
    {
        int cols = DataContext is DeckTool t ? t.Columns : 3, rows = DataContext is DeckTool r ? r.Rows : 1;
        var (tight, low, tiny, micro) = Room(Bounds.Width / Math.Max(1, cols), (Bounds.Height - 36) / Math.Max(1, rows));
        Classes.Set("tight", tight);
        Classes.Set("low", low);
        Classes.Set("tiny", tiny);
        Classes.Set("micro", micro);
    }

    public static (bool Tight, bool Low, bool Tiny, bool Micro) Room(double cardWidth, double cardHeight) =>
        cardWidth <= 0 && cardHeight <= 0 ? (false, false, false, false)
            : (cardWidth < 300, cardHeight < 235, cardHeight < 160, cardHeight < 110);
}
