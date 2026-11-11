using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Controls;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// One manual player (standby or PFL) as shown in a window. Two looks: compact (title, bar, buttons) or waveform
/// (waveform with cue markers and play head, a big counter that runs down and flashes in the last seconds).
/// </summary>
public sealed partial class PlayerCard : ObservableObject
{
    /// <summary>Raised when the look of all players changes (one setting for every player window).</summary>
    public static event Action? StyleChanged;

    public PlayerCard(string id)
    {
        Id = id;
        _wave = Hub.Settings.PlayerStyle != "compact";
        StyleChanged += () => Wave = Hub.Settings.PlayerStyle != "compact";
    }

    public string Id { get; }
    private long? _titleFor;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _label = "leeg";
    [ObservableProperty] private string _state = "empty";
    [ObservableProperty] private string _elapsed = "";
    [ObservableProperty] private string _remaining = "";
    [ObservableProperty] private string _intro = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private double _left;
    [ObservableProperty] private string _stack = "";
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private long? _mediaId;
    [ObservableProperty] private bool _wave;

    // waveform look
    [ObservableProperty] private ushort[]? _peaks;
    [ObservableProperty] private double _blockSeconds = 0.02;
    [ObservableProperty] private IReadOnlyList<WaveMarker> _markers = [];
    [ObservableProperty] private double _position = -1;
    [ObservableProperty] private double _cueIn;
    [ObservableProperty] private double _cueOut;
    [ObservableProperty] private string _endState = "normal";
    [ObservableProperty] private bool _flash;

    public string StateText => State switch { "playing" => "speelt", "paused" => "pauze", "cued" => "klaar", _ => "leeg" };

    // The state is never colour alone: a word and a sign go with it (design "Spelers").
    [ObservableProperty] private string _stateWord = "Leeg";
    [ObservableProperty] private string _stateGlyph = "–";
    [ObservableProperty] private IBrush _stateBrush = Brushes.FromHex("#6B7684");
    [ObservableProperty] private Thickness _stateBorder = new(1);
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private string _remainingTime = "—";
    [ObservableProperty] private bool _loaded;

    private void ShowState(bool loaded, double left)
    {
        var (word, glyph, color) = !loaded ? ("Leeg", "–", "#6B7684")
            : State == "playing" && left <= 10 ? ("Laatste 10 s", "!", "#F5A524")
            : State == "playing" ? ("Speelt", "▶", "#4C8DFF")
            : State == "paused" ? ("Pauze", "‖", "#9AA4B2")
            : ("Klaar", "■", "#3FB950");
        StateWord = word; StateGlyph = glyph; StateBrush = Brushes.FromHex(color);
        StateBorder = new Thickness(State == "playing" ? 2 : 1);
    }
    public string StyleButtonText => Wave ? "Compact" : "Golfvorm";
    partial void OnStateChanged(string value) => OnPropertyChanged(nameof(StateText));
    partial void OnWaveChanged(bool value) => OnPropertyChanged(nameof(StyleButtonText));
    partial void OnMediaIdChanged(long? value) => _ = LoadWaveAsync(value);

    public void Update(PlayerDto p)
    {
        Name = p.Name;
        State = p.State;
        Playing = p.State == "playing";
        MediaId = p.MediaId;
        Label = p.Label ?? "leeg — sleep een item hierheen";
        double len = Math.Max(0.1, p.CueOut - p.CueIn);
        double left = Math.Max(0, p.CueOut - p.Position);
        bool loaded = p.Label is not null;
        Elapsed = loaded ? StudioHub.Time(p.Position - p.CueIn) : "";
        Remaining = loaded ? "-" + StudioHub.Time(left) : "";
        Progress = loaded ? Math.Clamp((p.Position - p.CueIn) / len * 100, 0, 100) : 0;
        Left = loaded ? Math.Clamp(left / len * 100, 0, 100) : 0;
        Position = loaded ? p.Position : -1;
        CueIn = p.CueIn;
        CueOut = p.CueOut;
        EndState = !Playing ? "normal" : left <= 10 ? "end" : left <= 20 ? "warn" : "normal";
        Flash = EndState == "end" && DateTime.Now.Millisecond < 500;
        Intro = p.Intro is double i && i > p.Position && loaded ? $"intro {StudioHub.TimeTenths(i - p.Position)}" : "";
        Loaded = loaded;
        RemainingTime = !loaded ? "—" : Playing || State == "paused" ? "-" + StudioHub.Time(left) : StudioHub.Time(len);
        if (!loaded) { Title = "Sleep een item hierheen"; Sub = ""; }
        else if (string.IsNullOrEmpty(Title) || _titleFor != p.MediaId) Title = p.Label ?? "";
        ShowState(loaded, left);
        Stack = p.Stack.Count == 0 ? "" : $"daarna: {string.Join(", ", p.Stack.Take(3))}{(p.Stack.Count > 3 ? $" (+{p.Stack.Count - 3})" : "")}";
    }

    /// <summary>Fetches the waveform and cue points of the loaded item (only when the waveform look is used).</summary>
    private async Task LoadWaveAsync(long? id)
    {
        if (id is not long mediaId) { Peaks = null; Markers = []; Sub = ""; _titleFor = null; return; }
        var media = await Hub.Client.GetAsync<MediaDto>($"/api/media/{mediaId}");
        if (media is not null && MediaId == mediaId)
        {
            _titleFor = mediaId;
            Title = string.IsNullOrWhiteSpace(media.Title) ? Label : media.Title;
            Sub = string.Join(" · ", new[] { media.Artist, media.Category, StudioHub.Time(media.Cues.CueOut - media.Cues.CueIn),
                media.Lufs is double l ? $"{l:0.0} LUFS" : "" }.Where(t => !string.IsNullOrWhiteSpace(t)));
        }
        var peaks = await Hub.Client.GetAsync<PeaksDto>($"/api/media/{mediaId}/peaks");
        if (MediaId != mediaId) return; // another item was loaded in the meantime
        if (peaks is not null)
        {
            var bytes = Convert.FromBase64String(peaks.Data);
            var data = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, data, 0, data.Length * 2);
            BlockSeconds = peaks.BlockMs / 1000.0;
            Peaks = data;
        }
        else Peaks = null;
        Markers = media is null ? [] : MarkersFor(media.Cues);
    }

    /// <summary>The cue points a presenter needs while playing: intro, outro, start of the next item and the end.</summary>
    public static IReadOnlyList<WaveMarker> MarkersFor(CueDto c)
    {
        var list = new List<WaveMarker>();
        if (c.Intro is double intro) list.Add(new("intro", "Intro", intro, Color.Parse("#F0B429")));
        if (c.Outro is double outro) list.Add(new("outro", "Outro", outro, Color.Parse("#FB923C")));
        if (c.Mix > 0 && c.Mix < c.CueOut) list.Add(new("mix", "Volgende", c.Mix, Color.Parse("#E5484D")));
        list.Add(new("cueOut", "Uit", c.CueOut, Color.Parse("#A1A1AA")));
        return list;
    }

    private static StudioHub Hub => StudioHub.Current;

    [RelayCommand] private Task Toggle() => Hub.PlayerCommand(Id, "toggle");
    [RelayCommand] private Task Stop() => Hub.PlayerCommand(Id, "stop");
    [RelayCommand] private Task Eject() => Hub.PlayerCommand(Id, "eject");
    [RelayCommand] private Task ClearStack() => Hub.PlayerCommand(Id, "clear");
    [RelayCommand] private Task LoadSelected() => Hub.SelectedMedia is { } m ? Hub.LoadPlayer(Id, m.Id) : Task.CompletedTask;
    [RelayCommand] private Task EnqueueSelected() => Hub.SelectedMedia is { } m ? Hub.Client.PostAsync($"/api/players/{Id}/enqueue", new { mediaId = m.Id }) : Task.CompletedTask;
    [RelayCommand] private void EditCues() { if (MediaId is long id) Hub.RequestCueEditor(id); }

    /// <summary>Switches every player between the waveform and the compact look (saved in the studio settings).</summary>
    [RelayCommand]
    private void ToggleStyle()
    {
        Hub.Settings.PlayerStyle = Wave ? "compact" : "wave";
        Hub.Settings.Save();
        StyleChanged?.Invoke();
    }

    public Task Seek(double fraction, double cueIn, double cueOut) => Hub.PlayerCommand(Id, "seek", cueIn + fraction * (cueOut - cueIn));
    public Task SeekTo(double seconds) => Hub.PlayerCommand(Id, "seek", seconds);

    /// <summary>Drop: loads the item, or puts it on the stack when the player is busy.</summary>
    public Task Drop(long mediaId) => Hub.Client.PostAsync($"/api/players/{Id}/{(State == "playing" ? "enqueue" : "load")}", new { mediaId });
}

/// <summary>The standby players: manually started tracks on the on-air bus, next to the automation.</summary>
public sealed class StandbyTool : StudioTool
{
    public StandbyTool() : base("standby", "Standby-spelers") { }

    public ObservableCollection<PlayerCard> Players { get; } = [];

    protected override void OnState(StateDto s)
    {
        while (Players.Count > s.Players.Count) Players.RemoveAt(Players.Count - 1);
        for (int i = 0; i < s.Players.Count; i++)
        {
            if (i >= Players.Count || Players[i].Id != s.Players[i].Id)
            {
                if (i < Players.Count) Players.RemoveAt(i);
                Players.Insert(i, new PlayerCard(s.Players[i].Id));
            }
            Players[i].Update(s.Players[i]);
        }
    }
}

/// <summary>Pre-listen on the PFL bus (headphones), never on air.</summary>
public sealed class PflTool : StudioTool
{
    public PflTool() : base("pfl", "PFL") { }

    public PlayerCard Player { get; } = new("pfl");

    protected override void OnState(StateDto s) => Player.Update(s.Pfl);
}
