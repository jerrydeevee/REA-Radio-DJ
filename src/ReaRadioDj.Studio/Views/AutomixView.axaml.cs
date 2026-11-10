using Avalonia.Controls;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class AutomixView : UserControl
{
    public AutomixView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        // Drop a library track on the list: before the row it lands on, or at the end.
        MediaDrag.AcceptDrops(Drop, (id, e) =>
            DataContext is AutomixTool tool ? tool.Add(id, MediaDrag.ItemAt<AutomixRow>(Drop, e)?.Index) : Task.CompletedTask);
    }
}
