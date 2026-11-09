using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Controls;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A transition in the log: into item <see cref="ItemId"/> from the item before it.</summary>
public sealed record TransitionChoice(long ItemId, string Text, bool HandMade)
{
    public override string ToString() => Text;
}

/// <summary>A jingle or sweeper that can be laid over a transition (null id = none).</summary>
public sealed record InsertChoice(long? MediaId, string Text, double Length)
{
    public override string ToString() => Text;
}

/// <summary>
/// Transition editor: both waveforms on one timeline, where the next item starts, the volume lines of both items
/// and an optional insert. Pre-listen on PFL, then save it on the log item; the engine plays it that way on air.
/// </summary>
public sealed partial class TransitionTool : StudioTool
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public TransitionTool() : base("transition", "Overgangen")
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => FollowPlayhead();
    }

    public ObservableCollection<TransitionChoice> Choices { get; } = [];
    public ObservableCollection<InsertChoice> Inserts { get; } = [new(null, "geen insert", 0)];

    [ObservableProperty] private TransitionChoice? _choice;
    [ObservableProperty] private InsertChoice? _insert;
    [ObservableProperty] private TransitionState? _state;
    [ObservableProperty] private int _version;
    [ObservableProperty] private bool _dirty;
    [ObservableProperty] private bool _handMade;
    [ObservableProperty] private string _info = "";
    [ObservableProperty] private string? _error;

    private TransitionDto? _loaded;
    private long? _wanted;
    private bool _loading;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _played = new();
    private double _previewStart, _previewLength;

    public bool HasState => State is not null;
    public string StatusText => !HasState ? "" : Dirty ? "Gewijzigd, nog niet opgeslagen" : HandMade ? "Eigen overgang staat in de log" : "Automatische overgang";

    partial void OnStateChanged(TransitionState? value) => OnPropertyChanged(nameof(HasState));
    partial void OnDirtyChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnHandMadeChanged(bool value) => OnPropertyChanged(nameof(StatusText));

    partial void OnChoiceChanged(TransitionChoice? value)
    {
        if (value is not null && value.ItemId != _loaded?.ItemId) _ = LoadAsync(value.ItemId);
    }

    partial void OnInsertChanged(InsertChoice? value)
    {
        if (_loading || State is not { } s) return;
        s.InsertLabel = value?.MediaId is null ? null : value.Text;
        s.InsertLength = value?.Length ?? 0;
        if (value?.MediaId is not null && (s.InsertAt < s.OutCueIn || s.InsertAt <= 0)) s.InsertAt = s.StartAt - Math.Min(1, value.Length / 2);
        Changed();
    }

    /// <summary>Opens the transition into a log item (from the log's ⇄ button).</summary>
    public void Open(long itemId)
    {
        _wanted = itemId;
        Choice = Choices.FirstOrDefault(c => c.ItemId == itemId);
        if (Choice is null) _ = LoadAsync(itemId);
    }

    protected override void OnFirstShow()
    {
        Hub.TransitionRequested += Open;
        if (Hub.PendingTransition is long pending) Open(pending);
        _ = LoadInsertsAsync();
    }

    private async Task LoadInsertsAsync()
    {
        if (await Hub.Client.GetAsync<List<MediaDto>>("/api/library?kind=jingle&limit=1000") is not { } list) return;
        foreach (var m in list.Where(m => m.Enabled && !m.Missing).OrderBy(m => m.DisplayName))
            Inserts.Add(new InsertChoice(m.Id, m.DisplayName, Math.Max(0.1, m.Cues.CueOut - m.Cues.CueIn)));
    }

    protected override void OnState(StateDto s)
    {
        var up = s.Upcoming;
        var list = new List<TransitionChoice>();
        for (int i = 0; i < up.Count; i++)
        {
            string before = i > 0 ? Name(up[i - 1]) : s.Engine.Current is { } c ? $"{c.Label} (nu)" : "—";
            list.Add(new TransitionChoice(up[i].Id, $"{up[i].StartsAt:HH:mm}  {Short(before)}  →  {Short(Name(up[i]))}{(up[i].HandMadeMix ? "  ✎" : "")}",
                up[i].HandMadeMix));
        }
        if (list.SequenceEqual(Choices)) return;
        long? keep = Choice?.ItemId ?? _wanted;
        Choices.Clear();
        foreach (var x in list) Choices.Add(x);
        Choice = Choices.FirstOrDefault(x => x.ItemId == keep) ?? (keep is null ? Choices.FirstOrDefault() : null);
        if (Choice is null && _loaded is not null && !Dirty)
        {
            State = null;
            _loaded = null;
            Info = "Deze overgang staat niet meer in de log (al gespeeld of verwijderd).";
        }
    }

    private static string Name(LogItemDto u) => u.Type switch
    {
        "aiBreak" => "AI-break",
        "voiceTrack" => "voicetrack",
        _ => string.IsNullOrEmpty(u.Title) ? u.Label : $"{u.Artist} - {u.Title}".Trim(' ', '-'),
    };

    private static string Short(string s) => s.Length > 32 ? s[..31] + "…" : s;

    private async Task LoadAsync(long id)
    {
        Error = null;
        var dto = await Hub.Client.GetAsync<TransitionDto>($"/api/transition/{id}");
        if (dto is null) { Error = "Deze overgang kan niet bewerkt worden (een van de items heeft geen audio)."; return; }
        Show(dto);
    }

    private void Show(TransitionDto d)
    {
        _loading = true;
        _loaded = d;
        _wanted = null;
        var s = new TransitionState
        {
            OutPeaks = Decode(d.Out.Peaks), InPeaks = Decode(d.In.Peaks), BlockSeconds = d.BlockMs / 1000.0,
            OutLabel = d.Out.Label, InLabel = d.In.Label,
            OutCueIn = d.Out.CueIn, OutCueOut = d.Out.CueOut, OutMix = d.Out.Mix, InCueIn = d.In.CueIn, InCueOut = d.In.CueOut,
        };
        Fill(s, d.Settings);
        s.InsertLabel = d.InsertLabel;
        s.InsertLength = d.InsertLength;
        State = s;
        Insert = Inserts.FirstOrDefault(i => i.MediaId == d.Settings.InsertMediaId) ?? Inserts[0];
        if (d.Settings.InsertMediaId is not null && Insert.MediaId is null) { s.InsertLabel = d.InsertLabel; s.InsertLength = d.InsertLength; }
        HandMade = d.HandMade;
        Dirty = false;
        _loading = false;
        Describe();
    }

    private static void Fill(TransitionState s, TransitionSettingsDto t)
    {
        s.StartAt = t.StartAt;
        s.From = t.From;
        s.Out = t.Out.Select(p => (p.Seconds, p.Gain)).ToList();
        s.In = t.In.Select(p => (p.Seconds, p.Gain)).ToList();
        s.InsertAt = t.InsertAt;
    }

    private static ushort[] Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var data = new ushort[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, data, 0, data.Length * 2);
        return data;
    }

    /// <summary>Called by the view when an edit on the timeline is finished.</summary>
    public void Changed()
    {
        Dirty = true;
        Version++;
        Describe();
    }

    private void Describe()
    {
        if (State is not { } s) { Info = ""; return; }
        double vsMix = s.StartAt - s.OutMix;
        double overlap = s.OutCueOut - s.StartAt;
        string text = string.Format(Nl, "Volgende start op {0:0.00} s ({1}), overlap {2:0.0} s", s.StartAt,
            Math.Abs(vsMix) < 0.05 ? "op het mixpunt" : string.Format(Nl, "{0:+0.0;-0.0} s t.o.v. mixpunt", vsMix), Math.Max(0, overlap));
        if (Math.Abs(s.From - s.InCueIn) > 0.05) text += string.Format(Nl, " · begint {0:0.0} s in het nummer", s.From);
        if (s.InsertLabel is not null) text += string.Format(Nl, " · insert op {0:+0.0;-0.0} s", s.InsertAt - s.StartAt);
        Info = text;
        OnPropertyChanged(nameof(StatusText));
    }

    private TransitionSettingsDto? Current() => State is not { } s ? null : new(s.StartAt, s.From,
        s.Out.Select(p => new EnvelopePointDto(p.Seconds, p.Gain)).ToList(), s.In.Select(p => new EnvelopePointDto(p.Seconds, p.Gain)).ToList(),
        Insert?.MediaId, s.InsertAt);

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private async Task Listen()
    {
        if (_loaded is null || Current() is not { } body) return;
        Error = null;
        var r = await Hub.Client.SendForAsync<PreviewResult>(HttpMethod.Post, $"/api/transition/{_loaded.ItemId}/preview", body);
        if (r is null) { Error = "Voorbeeld kon niet gemaakt worden."; return; }
        _previewStart = r.Start;
        _previewLength = r.Length;
        _played.Restart();
        _timer.Start();
    }

    private sealed record PreviewResult(double Start, double Length);

    [RelayCommand]
    private async Task StopListen()
    {
        await Hub.Client.PostAsync("/api/transition/preview/stop");
        StopPlayhead();
    }

    private void FollowPlayhead()
    {
        if (State is not { } s) { StopPlayhead(); return; }
        double t = _played.Elapsed.TotalSeconds;
        if (t > _previewLength) { StopPlayhead(); return; }
        s.Playhead = _previewStart + t;
        Version++;
    }

    private void StopPlayhead()
    {
        _timer.Stop();
        _played.Reset();
        if (State is { } s) { s.Playhead = double.NaN; Version++; }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (_loaded is null || Current() is not { } body) return;
        var d = await Hub.Client.SendForAsync<TransitionDto>(HttpMethod.Put, $"/api/transition/{_loaded.ItemId}", body);
        if (d is null) { Error = "Opslaan mislukt: staat het item nog in de log?"; return; }
        Show(d);
        Hub.Status = "overgang opgeslagen";
    }

    /// <summary>Puts the automatic transition in the editor as a starting point (not saved yet).</summary>
    [RelayCommand]
    private void Automatic()
    {
        if (_loaded is null || State is not { } s) return;
        Fill(s, _loaded.Automatic);
        _loading = true;
        Insert = Inserts[0];
        s.InsertLabel = null;
        _loading = false;
        Changed();
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (_loaded is null) return;
        var d = await Hub.Client.SendForAsync<TransitionDto>(HttpMethod.Delete, $"/api/transition/{_loaded.ItemId}", null);
        if (d is not null) { Show(d); Hub.Status = "eigen overgang verwijderd"; }
    }

    [RelayCommand]
    private void FlatIn()
    {
        if (State is not { } s) return;
        s.In.Clear();
        Changed();
    }

    /// <summary>Fade-out of the outgoing item over a number of seconds from where the next item starts.</summary>
    [RelayCommand]
    private void FadeOut(string? seconds)
    {
        if (State is not { } s || !double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var len)) return;
        s.Out = len <= 0 ? [] : [(s.StartAt, 1), (Math.Min(s.OutCueOut, s.StartAt + len), 0)];
        Changed();
    }
}
