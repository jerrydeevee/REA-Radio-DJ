using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        MediaDrag.AcceptDropsAndFiles(List,
            (id, e) => DataContext is LogTool t ? t.Drop(id, MediaDrag.ItemAt<LogRow>(List, e)) : Task.CompletedTask,
            (path, e) => DataContext is LogTool t ? t.DropFile(path, MediaDrag.ItemAt<LogRow>(List, e)) : Task.CompletedTask);
    }
}
