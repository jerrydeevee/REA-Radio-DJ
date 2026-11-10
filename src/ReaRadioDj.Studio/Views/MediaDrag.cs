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
    /// <summary>An audio file by its path on the engine machine (from the Verkenner).</summary>
    public const string FileFormat = "application/x-rea-file";

    /// <summary>Drags a file; a file that is in the library also drags as a library item, so every target accepts it.</summary>
    public static async Task StartFileAsync(PointerEventArgs e, string path, long? mediaId)
    {
        var data = new DataObject();
        data.Set(FileFormat, path);
        if (mediaId is long id) data.Set(Format, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        data.Set(DataFormats.Text, System.IO.Path.GetFileNameWithoutExtension(path));
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    /// <summary>Like <see cref="AcceptDrops"/>, but also accepts files that are not in the library.</summary>
    public static void AcceptDropsAndFiles(Control target, Func<long, DragEventArgs, Task> onDrop, Func<string, DragEventArgs, Task> onFile)
    {
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(Format) || e.Data.Contains(FileFormat) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        target.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (e.Data.Get(Format) is string s && long.TryParse(s, out var id)) { e.Handled = true; await onDrop(id, e); }
            else if (e.Data.Get(FileFormat) is string path) { e.Handled = true; await onFile(path, e); }
        });
    }

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
