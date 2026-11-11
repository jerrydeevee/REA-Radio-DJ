using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using ReaRadioDj.Studio.ViewModels;

namespace ReaRadioDj.Studio.Views;

public partial class DjDeckView : UserControl
{
    public DjDeckView()
    {
        InitializeComponent();
        MediaDrag.AcceptDrops(this, (id, _) => DataContext is DjDeckVm deck ? deck.Load(id) : Task.CompletedTask);
        MediaDrag.DragSource(TitleArea, dc => dc is DjDeckVm { MediaId: long id } d ? (id, d.Title) : null, "deck");
        Wave.Clicked += seconds => { if (DataContext is DjDeckVm { Loaded: true } deck) _ = deck.SeekTo(seconds); };

        // Roll buttons work while held: pressed = roll on, released (or the pointer lost) = roll off.
        foreach (var b in RollBar.Children.OfType<Button>())
        {
            double beats = double.Parse((string)b.Tag!, CultureInfo.InvariantCulture);
            b.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed || DataContext is not DjDeckVm deck) return;
                e.Pointer.Capture(b);
                _ = deck.Roll(beats, true);
            }, handledEventsToo: true);
            b.AddHandler(PointerReleasedEvent, (_, _) => { if (DataContext is DjDeckVm deck) _ = deck.Roll(beats, false); }, handledEventsToo: true);
            b.AddHandler(PointerCaptureLostEvent, (_, _) => { if (DataContext is DjDeckVm deck) _ = deck.Roll(beats, false); }, handledEventsToo: true);
        }

        // Censor also works while held: pressed = backwards, released = on where the track would have been.
        CensorButton.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(CensorButton).Properties.IsLeftButtonPressed || DataContext is not DjDeckVm deck) return;
            e.Pointer.Capture(CensorButton);
            _ = deck.Censor(true);
        }, handledEventsToo: true);
        CensorButton.AddHandler(PointerReleasedEvent, (_, _) => { if (DataContext is DjDeckVm deck) _ = deck.Censor(false); }, handledEventsToo: true);
        CensorButton.AddHandler(PointerCaptureLostEvent, (_, _) => { if (DataContext is DjDeckVm deck) _ = deck.Censor(false); }, handledEventsToo: true);
    }
}
