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
        Cursor = media.Cues.CueIn;
        _loading = false;
        Dirty = false;
        MarkersChanged();
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
        Markers = Rows.Where(r => r.Value is not null).Select(r => new WaveMarker(r.Key, r.Label, r.Value!.Value, r.Color)).ToList();
        OnPropertyChanged(nameof(CueInValue));
        OnPropertyChanged(nameof(CueOutValue));
        if (!_loading && Media is not null) Dirty = true;
    }

    public void MoveMarker(string key, double seconds) => Cue(key).Value = seconds;

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
