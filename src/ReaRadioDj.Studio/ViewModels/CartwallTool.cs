using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed partial class CartButton(CartwallTool wall, int slot) : ObservableObject
{
    public int Slot { get; } = slot;
    public int Number => Slot % 16 + 1;
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private IBrush _background = Brushes.FromHex(null);
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private bool _overlay;
    [ObservableProperty] private long? _mediaId;
    public string Flags => (Loop ? "⟲ " : "") + (Overlay ? "◐" : "");
    partial void OnLoopChanged(bool value) => OnPropertyChanged(nameof(Flags));
    partial void OnOverlayChanged(bool value) => OnPropertyChanged(nameof(Flags));

    public CartDto? Data { get; set; }

    [RelayCommand] private Task Press() => wall.Press(this);
    [RelayCommand] private Task AssignSelected() => wall.AssignSelected(this);
    [RelayCommand] private Task ToggleLoop() => wall.Save(this, loop: !Loop);
    [RelayCommand] private Task ToggleOverlay() => wall.Save(this, overlay: !Overlay);
    [RelayCommand] private Task SetColor(string color) => wall.Save(this, color: color);
    [RelayCommand] private Task Clear() => wall.Clear(this);
    [RelayCommand] private void EditCues() { if (MediaId is long id) StudioHub.Current.RequestCueEditor(id); }
}

/// <summary>
/// A cartwall: four pages of sixteen buttons. Click plays, a second click fades out. Right click to assign the
/// library selection, set loop / overlay (ducks the automation) and a colour. Library items can be dropped on buttons.
/// </summary>
public sealed partial class CartwallTool : StudioTool
{
    public static readonly string[] Colors = ["#3FA7FF", "#3DBD7D", "#F0B429", "#A78BFA", "#E5484D", "#2DD4BF", "#F472B6", "#FB923C"];
    private List<CartDto> _carts = [];

    public CartwallTool() : base("cart1", "Cartwall 1")
    {
        for (int i = 0; i < 16; i++) Buttons.Add(new CartButton(this, i));
    }

    public int Wall => Id == "cart2" ? 1 : 0;
    public ObservableCollection<CartButton> Buttons { get; } = [];
    public IReadOnlyList<string> ColorChoices => Colors;
    [ObservableProperty] private int _page;
    [ObservableProperty] private string _playingText = "";

    public bool Page1 { get => Page == 0; set { if (value) Page = 0; } }
    public bool Page2 { get => Page == 1; set { if (value) Page = 1; } }
    public bool Page3 { get => Page == 2; set { if (value) Page = 2; } }
    public bool Page4 { get => Page == 3; set { if (value) Page = 3; } }

    partial void OnPageChanged(int value)
    {
        OnPropertyChanged(nameof(Page1)); OnPropertyChanged(nameof(Page2)); OnPropertyChanged(nameof(Page3)); OnPropertyChanged(nameof(Page4));
        Fill();
    }

    protected override void OnFirstShow() => _ = ReloadAsync();

    public async Task ReloadAsync()
    {
        var carts = await Hub.Client.GetAsync<List<CartDto>>($"/api/carts/{Wall}");
        if (carts is null) return;
        _carts = carts;
        Fill();
    }

    private void Fill()
    {
        for (int i = 0; i < 16; i++)
        {
            int slot = Page * 16 + i;
            var b = Buttons[i];
            var c = _carts.FirstOrDefault(x => x.Slot == slot);
            b.Data = c;
            b.MediaId = c?.MediaId;
            b.Label = c is null || c.MediaId is null ? "" : string.IsNullOrWhiteSpace(c.Label) ? c.MediaLabel ?? "" : c.Label;
            b.Sub = c is { MediaId: not null } ? StudioHub.Time(c.Length) : "";
            b.Loop = c?.Loop ?? false;
            b.Overlay = c?.Overlay ?? false;
            b.Background = Brushes.FromHex(c is { MediaId: not null } ? (string.IsNullOrWhiteSpace(c.Color) ? "#334155" : c.Color) : null);
        }
        if (Hub.State is { } s) OnState(s);
    }

    protected override void OnState(StateDto s)
    {
        var active = s.Carts.Where(c => c.Key.StartsWith($"{Wall}:", StringComparison.Ordinal))
            .ToDictionary(c => int.Parse(c.Key[(c.Key.IndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture));
        for (int i = 0; i < 16; i++)
        {
            var b = Buttons[i];
            if (active.TryGetValue(Page * 16 + i, out var clip))
            {
                b.Playing = true;
                b.Progress = clip.Length > 0 ? Math.Clamp(clip.Position / clip.Length * 100, 0, 100) : 0;
                b.Sub = "-" + StudioHub.Time(clip.Length - clip.Position);
            }
            else if (b.Playing)
            {
                b.Playing = false;
                b.Progress = 0;
                b.Sub = b.Data is { MediaId: not null } d ? StudioHub.Time(d.Length) : "";
            }
        }
        var others = active.Keys.Where(k => k / 16 != Page).Select(k => $"p{k / 16 + 1}:{k % 16 + 1}").ToList();
        PlayingText = active.Count == 0 ? "" : others.Count == 0 ? $"{active.Count} speelt" : $"{active.Count} speelt (ook {string.Join(", ", others)})";
    }

    public Task Press(CartButton b) => Hub.Client.PostAsync($"/api/carts/{Wall}/{Page * 16 + b.Slot}/press");

    public Task AssignSelected(CartButton b) => Hub.SelectedMedia is { } m ? Assign(b, m.Id) : Task.CompletedTask;

    public async Task Assign(CartButton b, long mediaId)
    {
        var d = b.Data;
        var color = d is { Color.Length: > 0 } ? d.Color : Colors[(Page * 16 + b.Slot) % Colors.Length];
        if (await Hub.Client.PutAsync($"/api/carts/{Wall}/{Page * 16 + b.Slot}", new CartRequest(mediaId, "", color, d?.Loop ?? false, d?.Overlay ?? false)))
            await ReloadAsync();
    }

    public async Task Save(CartButton b, bool? loop = null, bool? overlay = null, string? color = null)
    {
        if (b.Data is not { MediaId: long id } d) return;
        if (await Hub.Client.PutAsync($"/api/carts/{Wall}/{d.Slot}", new CartRequest(id, d.Label, color ?? d.Color, loop ?? d.Loop, overlay ?? d.Overlay)))
            await ReloadAsync();
    }

    public async Task Clear(CartButton b)
    {
        if (await Hub.Client.DeleteAsync($"/api/carts/{Wall}/{Page * 16 + b.Slot}")) await ReloadAsync();
    }

    [RelayCommand] private Task StopAll() => Hub.Client.PostAsync("/api/carts/stop");
    [RelayCommand] private Task Reload() => ReloadAsync();
}
