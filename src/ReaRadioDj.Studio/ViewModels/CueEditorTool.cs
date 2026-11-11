using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Controls;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed partial class CueRow(CueEditorTool editor, string key, string label, string color, bool optional, string help) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Help { get; } = help;
    public bool Optional { get; } = optional;
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(color));
    public Color Color { get; } = Color.Parse(color);

    [ObservableProperty] private double? _value;
    [ObservableProperty] private string _text = "";
    private bool _updating;

    partial void OnValueChanged(double? value)
    {
        _updating = true;
        Text = value is double v ? CueEditorTool.Format(v) : "";
        _updating = false;
        editor.MarkersChanged();
    }

    partial void OnTextChanged(string value)
    {
        if (_updating) return;
        if (string.IsNullOrWhiteSpace(value)) { if (Optional) Value = null; return; }
        if (CueEditorTool.Parse(value) is double v) Value = v;
    }

    [RelayCommand] private void SetHere() => Value = Math.Round(editor.Cursor, 3);
    [RelayCommand] private void Clear() { if (Optional) Value = null; }
    [RelayCommand] private Task Listen() => Value is double v ? editor.ListenFrom(Math.Max(0, Key is "mix" or "cueOut" or "fadeOut" or "outro" ? v - 5 : v)) : Task.CompletedTask;
    [RelayCommand] private void Go() { if (Value is double v) editor.Cursor = v; }
}

/// <summary>
/// An event at a place in the track: a volume change (quieter for a while, e.g. under a spoken intro) or a command on
/// the command bus (a lamp, an overlay, a cart). It goes off every time the track plays on air.
/// </summary>
public sealed partial class EventRow : ObservableObject
{
    private readonly CueEditorTool _editor;
    private bool _updating;

    public EventRow(CueEditorTool editor, TrackEventDto e)
    {
        _editor = editor;
        Kind = e.Kind;
        _at = e.At;
        _atText = CueEditorTool.Format(e.At);
        _text = e.Text ?? "";
        _gain = (decimal)e.Gain;
        _length = (decimal)e.Length;
        _ramp = (decimal)e.Ramp;
        _label = e.Label;
    }

    public string Kind { get; }
    public bool IsVolume => Kind == "volume";
    public bool IsCommand => !IsVolume;
    public string Title => IsVolume ? "Volume" : "Opdracht";
    public Color Color => Color.Parse(IsVolume ? "#38BDF8" : "#E879F9");
    public IBrush Brush => new SolidColorBrush(Color);
    /// <summary>Short text on the waveform.</summary>
    public string MarkerText => IsVolume ? $"{Gain ?? 0:+0;-0;0} dB" : (Text.Length > 12 ? Text[..12] + "…" : Text.Length == 0 ? "opdracht" : Text);

    [ObservableProperty] private double _at;
    [ObservableProperty] private string _atText;
    [ObservableProperty] private string _text;
    // decimal: what NumericUpDown binds to
    [ObservableProperty] private decimal? _gain;
    [ObservableProperty] private decimal? _length;
    [ObservableProperty] private decimal? _ramp;
    [ObservableProperty] private string _label;

    partial void OnAtChanged(double value)
    {
        _updating = true;
        AtText = CueEditorTool.Format(value);
        _updating = false;
        _editor.EventsChanged();
    }

    partial void OnAtTextChanged(string value)
    {
        if (!_updating && CueEditorTool.Parse(value) is double v) At = v;
    }

    partial void OnTextChanged(string value) { OnPropertyChanged(nameof(MarkerText)); _editor.EventsChanged(); }
    partial void OnGainChanged(decimal? value) { OnPropertyChanged(nameof(MarkerText)); _editor.EventsChanged(); }
    partial void OnLengthChanged(decimal? value) => _editor.EventsChanged();
    partial void OnRampChanged(decimal? value) => _editor.EventsChanged();
    partial void OnLabelChanged(string value) => _editor.EventsChanged();

    public TrackEventDto ToDto() => new(Kind, Math.Round(At, 3), IsCommand ? Text.Trim() : null,
        (double)(Gain ?? -6), (double)(Length ?? 5), (double)(Ramp ?? 0.5m), Label.Trim());

    [RelayCommand] private void SetHere() => At = Math.Round(_editor.Cursor, 3);
    [RelayCommand] private void Go() => _editor.Cursor = At;
    [RelayCommand] private Task Listen() => _editor.ListenFrom(Math.Max(0, At - 3));
    [RelayCommand] private void Remove() => _editor.RemoveEvent(this);
}

/// <summary>
/// The cue editor: waveform with all cue points (in, full level, intro, intro 2, outro, fade-out, mix, out, hook).
/// Drag the markers or type the times; listen on the PFL; save, or go back to the automatic points.
/// </summary>
public sealed partial class CueEditorTool : StudioTool
{
    public CueEditorTool() : base("cues", "Cue-editor")
    {
        Rows =
        [
            new(this, "cueIn", "In", "#3DBD7D", false, "eerste geluid; hier begint het item"),
            new(this, "fullLevel", "Vol", "#2DD4BF", true, "einde van de fade-in"),
            new(this, "intro", "Intro", "#F0B429", true, "einde intro: tot hier kan de presentator praten"),
            new(this, "intro2", "Intro 2", "#FBBF24", true, "tweede intro-punt (bijv. na de eerste zanglijn)"),
            new(this, "outro", "Outro", "#FB923C", true, "begin van het instrumentale einde"),
            new(this, "fadeOut", "Fade", "#F472B6", true, "hier begint de fade-out"),
            new(this, "mix", "Mix", "#E5484D", false, "hier start het volgende item"),
            new(this, "cueOut", "Uit", "#A1A1AA", false, "laatste geluid"),
            new(this, "hookIn", "Hook in", "#A78BFA", true, "begin herkenbaar fragment (teasers)"),
            new(this, "hookOut", "Hook uit", "#8B5CF6", true, "einde herkenbaar fragment"),
        ];
    }

    public IReadOnlyList<CueRow> Rows { get; }

    [ObservableProperty] private MediaDto? _media;
    [ObservableProperty] private ushort[]? _peaks;
    [ObservableProperty] private double _blockSeconds = 0.02;
    [ObservableProperty] private IReadOnlyList<WaveMarker> _markers = [];
    [ObservableProperty] private double _cursor;
    [ObservableProperty] private double _playhead = -1;
    [ObservableProperty] private string _heading = "Geen item gekozen";
    [ObservableProperty] private string _info = "";
    [ObservableProperty] private string _cursorText = "0:00.000";
    [ObservableProperty] private bool _dirty;

    /// <summary>Events at places in the track (volume changes, commands); saved together with the cue points.</summary>
    public ObservableCollection<EventRow> Events { get; } = [];
    [ObservableProperty] private bool _hasEvents;

    private bool _loading;

    partial void OnCursorChanged(double value) => CursorText = Format(value);

    protected override void OnFirstShow()
    {
        Hub.EditCuesRequested += id => _ = LoadAsync(id);
        if (Hub.SelectedMedia is { } m) _ = LoadAsync(m.Id);
    }

    protected override void OnState(StateDto s)
    {
        Playhead = Media is { } m && s.Pfl.MediaId == m.Id && s.Pfl.State is "playing" or "paused" ? s.Pfl.Position : -1;
    }

    public async Task LoadAsync(long id)
    {
        var media = await Hub.Client.GetAsync<MediaDto>($"/api/media/{id}");
        if (media is null) return;
        var peaks = await Hub.Client.GetAsync<PeaksDto>($"/api/media/{id}/peaks");
        _loading = true;
        Media = media;
        Heading = media.DisplayName;
        Info = $"{media.Category} · {Format(media.Duration)} · {(media.Lufs is double l ? $"{l:0.0} LUFS" : "luidheid onbekend")}" +
               (media.CuesManual ? " · handmatig aangepast" : " · automatisch bepaald");
        if (peaks is not null)
        {
            var bytes = Convert.FromBase64String(peaks.Data);
            var data = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, data, 0, data.Length * 2);
            BlockSeconds = peaks.BlockMs / 1000.0;
            Peaks = data;
        }
        SetRows(media.Cues);
        Events.Clear();
        foreach (var e in media.Events ?? []) Events.Add(new EventRow(this, e));
        HasEvents = Events.Count > 0;
        Cursor = media.Cues.CueIn;
        MarkersChanged();
        _loading = false;
        Dirty = false;
    }

    private void SetRows(CueDto c)
    {
        Cue("cueIn").Value = c.CueIn;
        Cue("fullLevel").Value = c.FullLevel;
        Cue("intro").Value = c.Intro;
        Cue("intro2").Value = c.Intro2;
        Cue("outro").Value = c.Outro;
        Cue("fadeOut").Value = c.FadeOut;
        Cue("mix").Value = c.Mix;
        Cue("cueOut").Value = c.CueOut;
        Cue("hookIn").Value = c.HookIn;
        Cue("hookOut").Value = c.HookOut;
    }

    private CueRow Cue(string key) => Rows.First(r => r.Key == key);

    public double CueInValue => Cue("cueIn").Value ?? 0;
    public double CueOutValue => Cue("cueOut").Value ?? Media?.Duration ?? 0;

    public void MarkersChanged()
    {
        Markers = Rows.Where(r => r.Value is not null).Select(r => new WaveMarker(r.Key, r.Label, r.Value!.Value, r.Color))
            .Concat(Events.Select((e, i) => new WaveMarker("ev:" + i, e.MarkerText, e.At, e.Color))).ToList();
        OnPropertyChanged(nameof(CueInValue));
        OnPropertyChanged(nameof(CueOutValue));
        if (!_loading && Media is not null) Dirty = true;
    }

    public void MoveMarker(string key, double seconds)
    {
        if (key.StartsWith("ev:", StringComparison.Ordinal))
        {
            if (int.TryParse(key[3..], out int i) && i >= 0 && i < Events.Count) Events[i].At = seconds;
            return;
        }
        Cue(key).Value = seconds;
    }

    public void EventsChanged()
    {
        HasEvents = Events.Count > 0;
        MarkersChanged();
    }

    public void RemoveEvent(EventRow row)
    {
        Events.Remove(row);
        EventsChanged();
    }

    /// <summary>A volume change at the cursor: 6 dB quieter for 5 seconds (e.g. under a spoken intro).</summary>
    [RelayCommand]
    private void AddVolume()
    {
        if (Media is null || Events.Count >= 32) return;
        Events.Add(new EventRow(this, new TrackEventDto("volume", Math.Round(Cursor, 3))));
        EventsChanged();
    }

    /// <summary>A command at the cursor (type the command line, as on the command bus).</summary>
    [RelayCommand]
    private void AddCommand()
    {
        if (Media is null || Events.Count >= 32) return;
        Events.Add(new EventRow(this, new TrackEventDto("command", Math.Round(Cursor, 3), "")));
        EventsChanged();
    }

    public async Task ListenFrom(double seconds)
    {
        if (Media is null) return;
        var pfl = Hub.State?.Pfl;
        if (pfl?.MediaId != Media.Id) await Hub.LoadPlayer("pfl", Media.Id);
        await Hub.PlayerCommand("pfl", "playfrom", seconds);
    }

    [RelayCommand] private Task PlayFromCursor() => ListenFrom(Cursor);
    [RelayCommand] private Task StopPfl() => Hub.PlayerCommand("pfl", "pause");
    [RelayCommand] private Task LoadSelected() => Hub.SelectedMedia is { } m ? LoadAsync(m.Id) : Task.CompletedTask;

    [RelayCommand]
    private async Task Save()
    {
        if (Media is null) return;
        var cues = new CueDto(Cue("cueIn").Value ?? 0, Cue("fullLevel").Value, Cue("intro").Value, Cue("intro2").Value, Cue("outro").Value,
            Cue("fadeOut").Value, Cue("mix").Value ?? 0, Cue("cueOut").Value ?? Media.Duration, Cue("hookIn").Value, Cue("hookOut").Value);
        var saved = await Hub.Client.SendForAsync<MediaDto>(HttpMethod.Put, $"/api/media/{Media.Id}/cues", cues);
        if (saved is null) return;
        var events = Events.Select(e => e.ToDto()).Where(e => e.Kind == "volume" || !string.IsNullOrWhiteSpace(e.Text)).ToList();
        saved = await Hub.Client.SendForAsync<MediaDto>(HttpMethod.Put, $"/api/media/{Media.Id}/events", events) ?? saved;
        await LoadAsync(saved.Id);
        Hub.Status = $"cues opgeslagen: {saved.DisplayName}";
        Hub.NotifyLibraryChanged();
    }

    [RelayCommand]
    private async Task Reset()
    {
        if (Media is null) return;
        var saved = await Hub.Client.SendForAsync<MediaDto>(HttpMethod.Post, $"/api/media/{Media.Id}/cues/reset", null);
        if (saved is not null) await LoadAsync(saved.Id);
    }

    [RelayCommand] private Task Revert() => Media is { } m ? LoadAsync(m.Id) : Task.CompletedTask;

    public static string Format(double s) => $"{(int)(s / 60)}:{s % 60:00.000}".Replace(',', '.');

    /// <summary>Parses "83.5", "1:23.5" or "1:23,5".</summary>
    public static double? Parse(string text)
    {
        text = text.Trim().Replace(',', '.');
        var parts = text.Split(':');
        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return null;
            total = total * 60 + v;
        }
        return total;
    }
}
