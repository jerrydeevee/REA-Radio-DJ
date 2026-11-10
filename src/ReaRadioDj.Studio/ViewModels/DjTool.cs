using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Controls;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One hot cue button of a deck.</summary>
public sealed partial class DjHotCueVm(int index, DjDeckVm deck) : ObservableObject
{
    public int Index { get; } = index;
    public DjDeckVm Deck { get; } = deck;
    public string Number => (Index + 1).ToString();
    [ObservableProperty] private bool _filled;
    [ObservableProperty] private string _time = "leeg";
}

/// <summary>
/// One DJ deck in the studio. Faders and knobs are sent to the engine as you move them; what the engine reports is
/// shown, except for a control you touched less than half a second ago (otherwise it would jump back under your hand).
/// </summary>
public sealed partial class DjDeckVm : ObservableObject
{
    private static readonly Color[] Colors = [Color.Parse("#4C8DFF"), Color.Parse("#F5A524"), Color.Parse("#3FB950"), Color.Parse("#B392F0")];
    private readonly Dictionary<string, DateTime> _touched = [];
    private bool _fromEngine;
    private long? _waveFor;

    public DjDeckVm(string id)
    {
        Id = id;
        Color = Colors[Math.Clamp(id[0] - 'A', 0, 3)];
        Brush = new SolidColorBrush(Color);
        HotCues = new ObservableCollection<DjHotCueVm>(Enumerable.Range(0, 8).Select(i => new DjHotCueVm(i, this)));
    }

    public string Id { get; }
    public Color Color { get; }
    public IBrush Brush { get; }
    public ObservableCollection<DjHotCueVm> HotCues { get; }
    private static StudioHub Hub => StudioHub.Current;

    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private string _title = "Leeg";
    [ObservableProperty] private string _artist = "sleep een nummer uit de bibliotheek hierheen";
    /// <summary>The state as a word: Leeg, Klaar, Speelt, In loop.</summary>
    [ObservableProperty] private string _stateText = "Leeg";
    [ObservableProperty] private string _bpmText = "---";
    [ObservableProperty] private string _tempoText = "+0,0 %";
    [ObservableProperty] private string _elapsed = "0:00";
    [ObservableProperty] private string _remaining = "-0:00";
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private ushort[]? _peaks;
    [ObservableProperty] private double _blockSeconds = 0.02;
    [ObservableProperty] private IReadOnlyList<WaveMarker> _markers = [];
    [ObservableProperty] private double _tempo;
    [ObservableProperty] private double _tempoRange = 8;
    [ObservableProperty] private bool _keyLock = true;
    [ObservableProperty] private bool _synced;
    [ObservableProperty] private double _fader = 100;
    [ObservableProperty] private double _gain;
    [ObservableProperty] private double _eqLow;
    [ObservableProperty] private double _eqMid;
    [ObservableProperty] private double _eqHigh;
    [ObservableProperty] private double _filter;
    [ObservableProperty] private bool _killLow;
    [ObservableProperty] private bool _killMid;
    [ObservableProperty] private bool _killHigh;
    [ObservableProperty] private bool _pfl;
    [ObservableProperty] private bool _loopActive;
    [ObservableProperty] private string _loopBeats = "4";
    [ObservableProperty] private double _levelLeft;
    [ObservableProperty] private double _levelRight;

    public string TempoRangeText => $"±{TempoRange:0} %";
    partial void OnTempoRangeChanged(double value) => OnPropertyChanged(nameof(TempoRangeText));

    // ---- controls you move: sent to the engine
    partial void OnTempoChanged(double value) => Send("tempo", value);
    partial void OnFaderChanged(double value) => Send("fader", value / 100);
    partial void OnGainChanged(double value) => Send("gain", value);
    partial void OnEqLowChanged(double value) => Send("eqlow", value);
    partial void OnEqMidChanged(double value) => Send("eqmid", value);
    partial void OnEqHighChanged(double value) => Send("eqhigh", value);
    partial void OnFilterChanged(double value) => Send("filter", value);

    private void Send(string action, double value)
    {
        if (_fromEngine) return;
        _touched[action] = DateTime.UtcNow;
        _ = Post(action, new DjCommand(Value: value));
    }

    private bool Free(string action) => !_touched.TryGetValue(action, out var at) || (DateTime.UtcNow - at).TotalMilliseconds > 500;

    private Task<bool> Post(string action, DjCommand? body = null) => Hub.Client.PostAsync($"/api/dj/decks/{Id}/{action}", body ?? new DjCommand());

    public Task Load(long mediaId) => Post("load", new DjCommand(MediaId: mediaId));
    public Task SeekTo(double seconds) => Post("seek", new DjCommand(Value: seconds));

    [RelayCommand] private Task Toggle() => Post("toggle");
    [RelayCommand] private Task Cue() => Post("cue");
    [RelayCommand] private Task Eject() => Post("eject");
    [RelayCommand] private Task ToggleKeyLock() => Post("keylock");
    [RelayCommand] private Task TogglePfl() => Post("pfl");
    [RelayCommand] private Task ResetTempo() => Post("tempo", new DjCommand(Value: 0));
    [RelayCommand] private Task NextRange() => Post("range", new DjCommand(Value: TempoRange switch { 8 => 12, 12 => 50, _ => 8 }));
    [RelayCommand] private Task Bend(string? direction) => Post("bend", new DjCommand(Value: direction == "-" ? -4 : 4));
    [RelayCommand] private Task Kill(string? band) => Post("kill" + band);
    [RelayCommand] private Task HotCue(DjHotCueVm? cue) => cue is null ? Task.CompletedTask : Post("hotcue", new DjCommand(Index: cue.Index));
    [RelayCommand] private Task ClearHotCue(DjHotCueVm? cue) => cue is null ? Task.CompletedTask : Post("hotcueclear", new DjCommand(Index: cue.Index));
    [RelayCommand] private Task LoopIn() => Post("loopin");
    [RelayCommand] private Task LoopOut() => Post("loopout");
    [RelayCommand] private Task Loop() => Post("loop");
    [RelayCommand] private Task LoopHalf() => Post("loophalf");
    [RelayCommand] private Task LoopDouble() => Post("loopdouble");
    [RelayCommand] private Task ResetEq() => Task.WhenAll(Post("eqlow", new(Value: 0)), Post("eqmid", new(Value: 0)), Post("eqhigh", new(Value: 0)), Post("filter", new(Value: 0)));

    [RelayCommand]
    private async Task Sync()
    {
        if (!await Post("sync")) Hub.Status = $"Deck {Id}: geen ander spelend deck met een tempo om te volgen";
    }

    public void Update(DjDeckDto d)
    {
        _fromEngine = true;
        try
        {
            Loaded = d.Loaded;
            Playing = d.Playing;
            Title = d.Loaded ? (d.Title.Length > 0 ? d.Title : "(zonder titel)") : "Leeg";
            Artist = d.Loaded ? d.Artist : "sleep een nummer uit de bibliotheek hierheen";
            StateText = !d.Loaded ? "Leeg" : d.LoopActive && d.Playing ? "In loop" : d.Playing ? "Speelt" : d.Position >= d.Duration - 0.1 ? "Afgelopen" : "Klaar";
            double bpm = d.Bpm * (1 + d.Tempo / 100);
            BpmText = d.Analysing ? "meten…" : d.Bpm > 0 ? bpm.ToString("0.0") : "---";
            TempoText = d.Tempo.ToString("+0.0;-0.0") + " %";
            Position = d.Position;
            Duration = d.Duration;
            Elapsed = StudioHub.Time(d.Position);
            Remaining = "-" + StudioHub.Time(Math.Max(0, d.Duration - d.Position));
            TempoRange = d.TempoRange;
            KeyLock = d.KeyLock;
            Synced = d.Synced;
            KillLow = d.KillLow; KillMid = d.KillMid; KillHigh = d.KillHigh;
            Pfl = d.Pfl;
            LoopActive = d.LoopActive;
            LoopBeats = d.LoopBeats >= 1 ? d.LoopBeats.ToString("0.##") : $"1/{1 / d.LoopBeats:0}";
            LevelLeft = d.LevelLeft * 100;
            LevelRight = d.LevelRight * 100;
            if (Free("tempo")) Tempo = d.Tempo;
            if (Free("fader")) Fader = d.Fader * 100;
            if (Free("gain")) Gain = d.Gain;
            if (Free("eqlow")) EqLow = d.EqLow;
            if (Free("eqmid")) EqMid = d.EqMid;
            if (Free("eqhigh")) EqHigh = d.EqHigh;
            if (Free("filter")) Filter = d.Filter;
            for (int i = 0; i < HotCues.Count && i < d.HotCues.Count; i++)
            {
                HotCues[i].Filled = d.HotCues[i] is not null;
                HotCues[i].Time = d.HotCues[i] is double at ? StudioHub.Time(at) : "leeg";
            }
            var markers = new List<WaveMarker> { new("cue", "cue", d.CuePoint, Color.Parse("#E6EAF0")) };
            for (int i = 0; i < d.HotCues.Count; i++)
                if (d.HotCues[i] is double at) markers.Add(new WaveMarker("hot" + i, (i + 1).ToString(), at, Color));
            if (d.LoopIn is double start) markers.Add(new WaveMarker("loopin", "in", start, Color.Parse("#3FB950")));
            if (d.LoopOut is double end) markers.Add(new WaveMarker("loopout", "uit", end, Color.Parse("#3FB950")));
            if (!Markers.SequenceEqual(markers)) Markers = markers;
        }
        finally { _fromEngine = false; }
        if (d.MediaId != _waveFor || (!d.Loaded && Peaks is not null))
        {
            _waveFor = d.MediaId;
            _ = LoadWaveAsync(d.MediaId);
        }
    }

    private async Task LoadWaveAsync(long? mediaId)
    {
        if (mediaId is not long id) { Peaks = null; return; }
        var peaks = await Hub.Client.GetAsync<PeaksDto>($"/api/media/{id}/peaks");
        if (_waveFor != id) return; // another track was loaded in the meantime
        if (peaks is null) { Peaks = null; return; }
        var bytes = Convert.FromBase64String(peaks.Data);
        var data = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, data, 0, data.Length * 2);
        BlockSeconds = peaks.BlockMs / 1000.0;
        Peaks = data;
    }
}

/// <summary>
/// The DJ desk: two or four decks, a mixer with equaliser, filter, faders and crossfader, and the switch that puts the
/// desk on air. Off air the decks are only heard on the pre-listen output.
/// </summary>
public sealed partial class DjTool : StudioTool
{
    private DateTime _crossTouched;
    private bool _fromEngine;

    public DjTool() : base("dj", "DJ-mengtafel")
    {
        Decks = [new DjDeckVm("A"), new DjDeckVm("B"), new DjDeckVm("C"), new DjDeckVm("D")];
    }

    public IReadOnlyList<DjDeckVm> Decks { get; }
    public DjDeckVm A => Decks[0];
    public DjDeckVm B => Decks[1];
    public DjDeckVm C => Decks[2];
    public DjDeckVm D => Decks[3];

    [ObservableProperty] private bool _onAir;
    [ObservableProperty] private bool _fourDecks;
    [ObservableProperty] private bool _sharp;
    /// <summary>0 (decks A and C) .. 100 (B and D).</summary>
    [ObservableProperty] private double _crossfader = 50;
    [ObservableProperty] private bool _available = true;
    [ObservableProperty] private bool _following;
    public bool CanFollow => Available && !OnAir && !Following;
    partial void OnFollowingChanged(bool value) => OnPropertyChanged(nameof(CanFollow));
    partial void OnOnAirChanged(bool value) => OnPropertyChanged(nameof(CanFollow));
    partial void OnAvailableChanged(bool value) => OnPropertyChanged(nameof(CanFollow));
    [ObservableProperty] private string _onAirText = "DJ-tafel is OFF AIR: je hoort de decks alleen op PFL";

    partial void OnCrossfaderChanged(double value)
    {
        if (_fromEngine) return;
        _crossTouched = DateTime.UtcNow;
        _ = Hub.Client.PostAsync("/api/dj/mixer", new DjMixerRequest(Crossfader: value / 100));
    }

    [RelayCommand] private Task SetOnAir(string? on) => Hub.Client.PostAsync("/api/dj/mixer", new DjMixerRequest(OnAir: on == "on"));
    [RelayCommand] private Task ToggleCurve() => Hub.Client.PostAsync("/api/dj/mixer", new DjMixerRequest(Curve: Sharp ? "smooth" : "sharp"));
    [RelayCommand] private void SetDecks(string? count) => FourDecks = count == "4";

    [ObservableProperty] private bool _recording;
    [ObservableProperty] private string _recordText = "● Mix opnemen";

    /// <summary>Starts or stops recording the mix; a finished mix lands in the library under Opnamen.</summary>
    [RelayCommand]
    private async Task ToggleRecord()
    {
        if (!await Hub.Client.PostAsync("/api/dj/mixer", new DjMixerRequest(Record: !Recording)))
            Hub.Status = "Opnemen van de mix lukte niet; kijk bij Meldingen";
    }
    [RelayCommand] private void CenterCrossfader() => Crossfader = 50;

    /// <summary>Deck A starts copying the track on air again (it must be empty).</summary>
    [RelayCommand]
    private async Task Follow()
    {
        if (!await Hub.Client.PostAsync("/api/dj/follow")) Hub.Status = "Deck A kan de automatisering niet volgen: haal eerst het nummer van deck A, en de tafel mag niet ON AIR zijn";
    }

    protected override void OnState(StateDto s)
    {
        Available = s.Dj is not null;
        if (s.Dj is not { } dj) return;
        _fromEngine = true;
        try
        {
            OnAir = dj.OnAir;
            Following = dj.Following;
            OnAirText = dj.OnAir ? "DJ-tafel is ON AIR: wat je mixt gaat de zender op"
                : dj.Following ? "Deck A volgt de automatisering. Kies DJ ON AIR om naadloos over te nemen; de automatisering stopt dan."
                : "DJ-tafel is OFF AIR: je hoort de decks alleen op PFL";
            Sharp = dj.Curve == "sharp";
            Recording = dj.Recording is not null;
            RecordText = dj.Recording is null ? "● Mix opnemen" : $"■ Stop opname {TimeSpan.FromSeconds(dj.RecordingSeconds):h\\:mm\\:ss}";
            if ((DateTime.UtcNow - _crossTouched).TotalMilliseconds > 500) Crossfader = dj.Crossfader * 100;
        }
        finally { _fromEngine = false; }
        foreach (var d in dj.Decks)
            Decks.FirstOrDefault(x => x.Id == d.Id)?.Update(d);
    }
}
