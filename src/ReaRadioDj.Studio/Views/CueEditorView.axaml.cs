using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class CueEditorView : UserControl
{
    public CueEditorView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        Wave.MarkerMoved += (key, seconds) => (DataContext as CueEditorTool)?.MoveMarker(key, seconds);
        Wave.DoubleTapped += (_, _) => Wave.ResetZoom();
        MediaDrag.AcceptDrops(Wave, (id, _) => DataContext is CueEditorTool t ? t.LoadAsync(id) : Task.CompletedTask);
        // A low panel keeps the waveform: the info line goes, and the cue rows and events scroll in at most half the height.
        SizeChanged += (_, e) =>
        {
            double h = e.NewSize.Height;
            InfoText.IsVisible = h >= 300;
            RowsScroll.MaxHeight = Math.Clamp(h * 0.5, 40, 420);
        };
    }
}
