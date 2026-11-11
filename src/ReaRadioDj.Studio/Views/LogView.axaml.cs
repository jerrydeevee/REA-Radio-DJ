using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        // A log item can be dragged to the clipboard, a player or a deck (not back into the log: that would copy it).
        MediaDrag.DragSource(List, dc => dc is LogRow { MediaId: long id } r ? (id, r.Label) : null, "log");
        MediaDrag.AcceptDropsAndFiles(List,
            (id, e) => DataContext is LogTool t && MediaDrag.SourceOf(e) != "log" ? t.Drop(id, MediaDrag.ItemAt<LogRow>(List, e)) : Task.CompletedTask,
            (path, e) => DataContext is LogTool t ? t.DropFile(path, MediaDrag.ItemAt<LogRow>(List, e)) : Task.CompletedTask);
        // A low panel keeps its list: the now-playing card goes first (the on-air bar shows it too), then "Gespeeld".
        SizeChanged += (_, e) =>
        {
            NowCard.IsVisible = e.NewSize.Height >= 300;
            Played.IsVisible = e.NewSize.Height >= 200;
        };
    }
}
