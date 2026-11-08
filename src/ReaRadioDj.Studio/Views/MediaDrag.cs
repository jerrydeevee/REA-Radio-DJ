using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

/// <summary>Drag and drop of library items between windows (also between floating windows).</summary>
public static class MediaDrag
{
    public const string Format = "application/x-rea-media";

    public static async Task StartAsync(PointerEventArgs e, MediaDto media)
    {
        var data = new DataObject();
        data.Set(Format, media.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        data.Set(DataFormats.Text, media.DisplayName);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    /// <summary>Makes a control a drop target for library items; the handler gets the media id and the drop event.</summary>
    public static void AcceptDrops(Control target, Func<long, DragEventArgs, Task> onDrop)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(Format) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        target.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (e.Data.Get(Format) is string s && long.TryParse(s, out var id))
            {
                e.Handled = true;
                await onDrop(id, e);
            }
        });
    }

    /// <summary>Finds the data context of type T under the pointer of a drop event.</summary>
    public static T? ItemAt<T>(Control root, DragEventArgs e) where T : class
    {
        var hit = root.InputHitTest(e.GetPosition(root)) as StyledElement;
        while (hit is not null && !ReferenceEquals(hit, root))
        {
            if (hit.DataContext is T t) return t;
            hit = hit.Parent as StyledElement ?? (hit as Visual)?.GetVisualParentElement();
        }
        return null;
    }

    private static StyledElement? GetVisualParentElement(this Visual v) => Avalonia.VisualTree.VisualExtensions.GetVisualParent(v) as StyledElement;

    /// <summary>Wires a view to its window: subscribes the tool to the live state when the view is shown.</summary>
    public static void Bind(UserControl view)
    {
        view.AttachedToVisualTree += (_, _) => (view.DataContext as StudioTool)?.Attach();
        view.DataContextChanged += (_, _) => { if (TopLevel.GetTopLevel(view) is not null) (view.DataContext as StudioTool)?.Attach(); };
    }
}
