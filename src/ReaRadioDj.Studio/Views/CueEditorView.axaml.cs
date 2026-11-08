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
    }
}
