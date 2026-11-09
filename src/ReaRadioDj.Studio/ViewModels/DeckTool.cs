using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Controls;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Colours of the three decks (on air, next, after that) and of the big counters. One palette for the whole studio:
/// the deck window, the log and anything else that marks an item by deck. Chosen in the deck window, saved in the settings.
/// </summary>
public static class DeckPalette
{
    public sealed record Preset(string Name, string A, string B, string C, string Counter);

    public static readonly Preset[] Presets =
    [
        new("Studio (rood · amber · blauw)", "#E5484D", "#F0B429", "#3FA7FF", "#F5F7FA"),
        new("Verkeerslicht (rood · groen · blauw)", "#E5484D", "#3DBD7D", "#3FA7FF", "#F5F7FA"),
        new("Neon", "#FF3D7F", "#00E5A0", "#00B3FF", "#FFE45C"),
        new("Warm", "#FB923C", "#FBBF24", "#A78BFA", "#FDE68A"),
        new("Rustig (grijstinten)", "#CBD5E1", "#94A3B8", "#64748B", "#F5F7FA"),
    ];

    public static event Action? Changed;

    private static StudioSettings S => StudioHub.Current.Settings;
    public static string Hex(int deck) => S.DeckColors is { Length: 3 } c ? c[Math.Clamp(deck, 0, 2)] : Presets[0].A;
    public static IBrush Brush(int deck) => Brushes.FromHex(Hex(deck));
    public static IBrush Counter => Brushes.FromHex(S.CounterColor);
    public static string Letter(int deck) => deck switch { 0 => "A", 1 => "B", _ => "C" };

    public static void Apply(Preset p)
    {
        S.DeckColors = [p.A, p.B, p.C];
        S.CounterColor = p.Counter;
        S.Save();
        Changed?.Invoke();
    }
}

/// <summary>Waveform data of one library item: peaks and cue points.</summary>
public static class WaveData
{
    public static async Task<(ushort[]? Peaks, double BlockSeconds, CueDto? Cues)> LoadAsync(long id)
    {
        var hub = StudioHub.Current;
        var media = await hub.Client.GetAsync<MediaDto>($"/api/media/{id}");
        var peaks = await hub.Client.GetAsync<PeaksDto>($"/api/media/{id}/peaks");
        if (peaks is null) return (null, 0.02, media?.Cues);
        var bytes = Convert.FromBase64String(peaks.Data);
        var data = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, data, 0, data.Length * 2);
        return (data, peaks.BlockMs / 1000.0, media?.Cues);
    }
}

/// <summary>One deck: the item on air (A), the next one (B) or the one after that (C).</summary>
public sealed partial class DeckVm(int index) : ObservableObject
{
    public int Index { get; } = index;
    public string Letter => DeckPalette.Letter(Index);
    public bool IsOnAir => Index == 0;
    public bool IsNext => Index == 1;
    [ObservableProperty] private IBrush _brush = DeckPalette.Brush(index);
    [ObservableProperty] private IBrush _counterBrush = DeckPalette.Counter;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private string _counter = "--:--";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private string _intro = "";
    [ObservableProperty] private double _left;
    [ObservableProperty] private string _endState = "normal";
    [ObservableProperty] private bool _flash;
    [ObservableProperty] private bool _empty = true;
    [ObservableProperty] private ushort[]? _peaks;
    [ObservableProperty] private double _blockSeconds = 0.02;
    [ObservableProperty] private IReadOnlyList<WaveMarker> _markers = [];
    [ObservableProperty] private double _position = -1;
    [ObservableProperty] private double _cueIn;
    [ObservableProperty] private double _cueOut;
    private long _mediaId;
    /// <summary>Log item id for B and C (to remove it); 0 for A.</summary>
    public long LogId { get; private set; }

    public void Recolor()
    {
        Brush = DeckPalette.Brush(Index);
        CounterBrush = DeckPalette.Counter;
    }

    public void Clear(string status)
    {
        Empty = true; Status = status; Title = ""; Artist = ""; Type = ""; Counter = "--:--"; Sub = ""; Intro = "";
        Left = 0; EndState = "normal"; Flash = false; Position = -1; Peaks = null; Markers = []; _mediaId = 0;
    }

    public void OnAir(NowPlayingDto c, DateTime now)
    {
        Empty = false;
        Status = "OP DE LUCHT";
        Title = string.IsNullOrWhiteSpace(c.Title) ? c.Label : c.Title;
        Artist = c.Artist;
        Type = c.Category;
        double left = Math.Max(0, c.To - c.Position), length = Math.Max(0.1, c.To - c.From);
        Counter = "-" + StudioHub.Time(left);
        Sub = $"{StudioHub.Time(c.Position - c.From)} / {StudioHub.Time(length)}";
        Left = Math.Clamp(left / length * 100, 0, 100);
        EndState = left <= 10 ? "end" : left <= 20 ? "warn" : "normal";
        Flash = EndState == "end" && now.Millisecond < 500;
        Intro = c.Intro is double i && c.Position < i ? $"INTRO {Math.Ceiling(i - c.Position):0}" : "";
        // the counter keeps the palette colour, but turns amber in the last 20 s and blinks red in the last 10 s
        CounterBrush = EndState switch
        {
            "end" => Brushes.FromHex(Flash ? "#FFFFFF" : "#E5484D"),
            "warn" => Brushes.FromHex("#F0B429"),
            _ => DeckPalette.Counter,
        };
        Position = c.Position;
        CueIn = c.From;
        CueOut = c.To;
        Load(c.MediaId);
    }

    public void Coming(LogItemDto u, bool next)
    {
        Empty = false;
        LogId = u.Id;
        Status = next ? $"VOLGENDE · {u.StartsAt:HH:mm:ss}" : $"DAARNA · {u.StartsAt:HH:mm:ss}";
        Title = string.IsNullOrWhiteSpace(u.Title) ? u.Label : u.Title!;
        Artist = u.Artist ?? "";
        Type = u.Category ?? u.Type;
        Counter = StudioHub.Time(u.Length);
        Sub = u.Origin;
        Left = 100;
        EndState = "normal";
        Flash = false;
        Intro = "";
        Position = -1;
        CounterBrush = DeckPalette.Counter;
        if (u.MediaId is long id) Load(id); else { Peaks = null; Markers = []; _mediaId = 0; }
    }

    private void Load(long id)
    {
        if (id == _mediaId || id <= 0) return;
        _mediaId = id;
        _ = LoadAsync(id);
    }

    private async Task LoadAsync(long id)
    {
        var (peaks, block, cues) = await WaveData.LoadAsync(id);
        if (_mediaId != id) return;
        BlockSeconds = block;
        Peaks = peaks;
        Markers = cues is null ? [] : PlayerCard.MarkersFor(cues);
        if (!IsOnAir && cues is not null) { CueIn = cues.CueIn; CueOut = cues.CueOut; }
    }
}

/// <summary>
/// Live decks: the item on air and the next two side by side, each with its own colour, waveform and a big counter.
/// The colours (and the counter colour) are a palette the user picks; the log uses the same colours.
/// </summary>
public sealed partial class DeckTool : StudioTool
{
    public DeckTool() : base("decks", "Live-decks")
    {
        DeckPalette.Changed += () => { foreach (var d in Decks) d.Recolor(); };
        _horizontal = Hub.Settings.DecksHorizontal;
    }

    public ObservableCollection<DeckVm> Decks { get; } = [new(0), new(1), new(2)];
    public IReadOnlyList<string> PaletteNames => DeckPalette.Presets.Select(p => p.Name).ToList();
    [ObservableProperty] private int _palette = -1;
    [ObservableProperty] private bool _horizontal;
    [ObservableProperty] private bool _showOptions;
    public int Columns => Horizontal ? 3 : 1;
    public int Rows => Horizontal ? 1 : 3;

    partial void OnPaletteChanged(int value)
    {
        if (value >= 0 && value < DeckPalette.Presets.Length) DeckPalette.Apply(DeckPalette.Presets[value]);
    }

    partial void OnHorizontalChanged(bool value)
    {
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Rows));
        Hub.Settings.DecksHorizontal = value;
        Hub.Settings.Save();
    }

    protected override void OnState(StateDto s)
    {
        if (s.Engine.Current is { } c) Decks[0].OnAir(c, s.Time);
        else Decks[0].Clear(s.Engine.AutoPlay ? "WACHTEN" : "STIL");
        for (int i = 1; i < 3; i++)
        {
            if (s.Upcoming.Count >= i) Decks[i].Coming(s.Upcoming[i - 1], i == 1);
            else Decks[i].Clear("LEEG");
        }
    }

    [RelayCommand] private void ToggleOptions() => ShowOptions = !ShowOptions;
    [RelayCommand] private Task Next() => Hub.Client.RunCommandAsync("NEXT");
    [RelayCommand] private Task Stop() => Hub.Client.RunCommandAsync("STOP");
    [RelayCommand] private Task Skip() => Hub.Client.RunCommandAsync("SKIP");
    [RelayCommand] private Task Remove(DeckVm? d) => d is { LogId: > 0 } ? Hub.Client.DeleteAsync($"/api/log/{d.LogId}") : Task.CompletedTask;
}
