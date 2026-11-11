using Avalonia.Controls;
using Avalonia.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class ClipboardView : UserControl
{
    public ClipboardView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        SizeChanged += (_, e) => Classes.Set("tight", e.NewSize.Width < 340);
        // Anything that drags as a library item can be parked here; the clipboard's own items only move.
        MediaDrag.AcceptDrops(this, (id, e) =>
            DataContext is ClipboardTool t && MediaDrag.SourceOf(e) != "clipboard" ? t.Drop(id) : Task.CompletedTask);
        MediaDrag.DragSource(List, dc => dc is MediaDto m ? (m.Id, m.DisplayName) : null, "clipboard");
        // Delete removes the chosen track from the clipboard.
        List.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && DataContext is ClipboardTool t && List.SelectedItem is MediaDto m)
            {
                t.RemoveCommand.Execute(m);
                e.Handled = true;
            }
        };
    }
}
