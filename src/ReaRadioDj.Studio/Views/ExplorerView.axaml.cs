using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class ExplorerView : UserControl
{
    private Point? _pressed;
    private ExplorerEntry? _pressedItem;

    public ExplorerView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);

        PlaceList.SelectionChanged += (_, _) =>
        {
            if (PlaceList.SelectedItem is ExplorerPlace p && DataContext is ExplorerTool t)
            {
                t.GoCommand.Execute(p);
                PlaceList.SelectedItem = null;
            }
        };

        // Drag an audio file to the log, a player or a cartwall: starts after a few pixels of movement.
        Files.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(Files).Properties.IsLeftButtonPressed) return;
            _pressed = e.GetPosition(Files);
            _pressedItem = (e.Source as StyledElement)?.DataContext as ExplorerEntry;
        }, handledEventsToo: true);
        Files.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (_pressed is not Point p || _pressedItem is not { IsFolder: false } item) return;
            var now = e.GetPosition(Files);
            if (Math.Abs(now.X - p.X) + Math.Abs(now.Y - p.Y) < 8) return;
            _pressed = null;
            _pressedItem = null;
            await MediaDrag.StartFileAsync(e, item.Path, item.Dto.MediaId);
        }, handledEventsToo: true);
        Files.AddHandler(PointerReleasedEvent, (_, _) => { _pressed = null; _pressedItem = null; }, handledEventsToo: true);

        // Double click or Enter: open a folder or put the file at the end of the log; Backspace: one folder up.
        Files.DoubleTapped += (_, _) => { if (DataContext is ExplorerTool { Selected: { } s } t) _ = t.Activate(s); };
        Files.KeyDown += (_, e) =>
        {
            if (DataContext is not ExplorerTool t) return;
            if (e.Key == Key.Enter && t.Selected is { } s) { _ = t.Activate(s); e.Handled = true; }
            else if (e.Key == Key.Back) { t.UpCommand.Execute(null); e.Handled = true; }
        };
    }
}
