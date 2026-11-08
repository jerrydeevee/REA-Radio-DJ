using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class LibraryView : UserControl
{
    private Point? _pressed;
    private MediaDto? _pressedItem;

    public LibraryView()
    {
        InitializeComponent();
        MediaDrag.Bind(this);
        // Drag an item out of the list (to the log, a player or a cartwall): starts after a few pixels of movement.
        List.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(List).Properties.IsLeftButtonPressed) return;
            _pressed = e.GetPosition(List);
            _pressedItem = (e.Source as StyledElement)?.DataContext as MediaDto;
        }, handledEventsToo: true);
        List.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (_pressed is not Point p || _pressedItem is null) return;
            var now = e.GetPosition(List);
            if (Math.Abs(now.X - p.X) + Math.Abs(now.Y - p.Y) < 8) return;
            var item = _pressedItem;
            _pressed = null;
            _pressedItem = null;
            await MediaDrag.StartAsync(e, item);
        }, handledEventsToo: true);
        List.AddHandler(PointerReleasedEvent, (_, _) => { _pressed = null; _pressedItem = null; }, handledEventsToo: true);
        List.DoubleTapped += (_, _) => (DataContext as LibraryTool)?.PflCommand.Execute(null);
    }
}
