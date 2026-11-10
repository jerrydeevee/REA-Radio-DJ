using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
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
        // Small dock panel: the list always keeps room; the top scrolls, the detail pane only shows when it fits.
        SizeChanged += (_, e) =>
        {
            double h = e.NewSize.Height, w = e.NewSize.Width;
            TopArea.MaxHeight = Math.Max(60, h * 0.45);
            if (DataContext is LibraryTool t) t.Wide = w >= 720;
        };
        DataContextChanged += (_, _) => { if (DataContext is LibraryTool t) t.Wide = Bounds.Width <= 0 || Bounds.Width >= 720; };
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
        List.DoubleTapped += (_, _) => (DataContext as LibraryTool)?.DoubleClickSelected();
        List.SelectionChanged += (_, _) => (DataContext as LibraryTool)?.SetSelection(List.SelectedItems?.OfType<MediaDto>() ?? []);
        // Keys in the list: Enter = next, Ctrl+Enter = at the end, space = pre-listen, Esc = stop pre-listening.
        List.KeyDown += (_, e) =>
        {
            if (DataContext is not LibraryTool t) return;
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            switch (e.Key)
            {
                case Key.Enter when ctrl: t.EndCommand.Execute(null); e.Handled = true; break;
                case Key.Enter: t.NextCommand.Execute(null); e.Handled = true; break;
                case Key.Space: t.PflCommand.Execute(null); e.Handled = true; break;
                case Key.Escape: t.PflStopCommand.Execute(null); e.Handled = true; break;
                case Key.F when ctrl: SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; break;
            }
        };
        // Just start typing in the list: the letters go to the search box.
        List.TextInput += (_, e) =>
        {
            if (DataContext is not LibraryTool t || string.IsNullOrWhiteSpace(e.Text) || e.Text == " ") return;
            t.Query = e.Text;
            SearchBox.Focus();
            SearchBox.CaretIndex = t.Query.Length;
            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
            // Ctrl+Z outside a text box undoes the last library change (in a text box it stays the normal text undo).
            else if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control && e.Source is not TextBox && DataContext is LibraryTool t && t.CanUndo)
            { t.UndoCommand.Execute(null); e.Handled = true; }
        };
        // Folder picker for a library folder (also network locations the Windows dialog offers).
        BrowseFolder.Click += async (_, _) =>
        {
            if (DataContext is not LibraryTool t || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
            var picked = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Map voor de bibliotheek", AllowMultiple = false });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) t.NewFolderPath = path;
        };
        // Esc empties the search box; arrow down jumps from the search box into the list.
        SearchBox.KeyDown += (_, e) =>
        {
            if (DataContext is not LibraryTool t) return;
            if (e.Key == Key.Escape) { t.Query = ""; e.Handled = true; }
            else if (e.Key == Key.Down && t.Items.Count > 0)
            {
                t.Selected ??= t.Items[0];
                List.Focus();
                e.Handled = true;
            }
        };
    }

    // Picks a picture and puts it into the file of the item in the detail pane as its cover.
    private async void ChooseCover(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not MediaDetailVm detail || TopLevel.GetTopLevel(this)?.StorageProvider is not { } sp) return;
        var picked = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Hoes kiezen", AllowMultiple = false,
            FileTypeFilter = [new("Afbeeldingen") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp"] }],
        });
        if (picked.Count == 0) return;
        await using var stream = await picked[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        await detail.SetCoverAsync(ms.ToArray());
    }
}
