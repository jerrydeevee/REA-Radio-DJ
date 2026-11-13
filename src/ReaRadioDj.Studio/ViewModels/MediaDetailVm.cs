using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Controls;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// The detail pane next to the library list: waveform with the cues, all tags (editable in place), the measured beat
/// and markers, why it may not play now, and the last times it went on air.
/// </summary>
public sealed partial class MediaDetailVm : ObservableObject
{
    private readonly LibraryTool _owner;

    public MediaDetailVm(LibraryTool owner, MediaDto m)
    {
        _owner = owner;
        Media = m;
        _artist = m.Artist; _title = m.Title; _album = m.Album; _genre = m.Genre; _year = m.Year?.ToString() ?? "";
        _language = m.Language; _mood = m.Mood;
        _rating = Stars.FirstOrDefault(s => s.Value == (m.Rating ?? 0)) ?? Stars[0];
        _energyLevel = Energies.FirstOrDefault(s => s.Value == (m.EnergyLevel ?? 0)) ?? Energies[0];
        _ = LoadAsync();
    }

    public MediaDto Media { get; private set; }
    public long Id => Media.Id;

    [ObservableProperty] private string _artist;
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _album;
    [ObservableProperty] private string _genre;
    [ObservableProperty] private string _year;
    [ObservableProperty] private string _language;
    [ObservableProperty] private string _mood;
    [ObservableProperty] private LevelChoice _rating;
    [ObservableProperty] private LevelChoice _energyLevel;

    public static List<LevelChoice> Stars { get; } =
        [new(0, "geen"), new(1, "★"), new(2, "★★"), new(3, "★★★"), new(4, "★★★★"), new(5, "★★★★★")];
    public static List<LevelChoice> Energies { get; } =
        [new(0, "niet gezet"), new(1, "1 rustig"), new(2, "2"), new(3, "3 midden"), new(4, "4"), new(5, "5 knallen")];

    /// <summary>What the quality check found, one line each (empty = nothing).</summary>
    public IReadOnlyList<string> Issues => Media.Issues ?? [];
    public bool HasIssues => Media.HasIssues;
    [ObservableProperty] private string _saveText = "";
    /// <summary>What the presenter says with this item; shown large in the on-air bar while it plays.</summary>
    [ObservableProperty] private string _presenterText = "";
    [ObservableProperty] private string _presenterSaveText = "";
    [ObservableProperty] private ushort[]? _peaks;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _cover;

    /// <summary>A jingle without a picture of its own gets the standard jingle cover.</summary>
    public bool ShowJingleCover => Cover is null && IsJingle;
    public bool IsJingle => Media.Kind == "jingle";
    public string KindButtonText => IsJingle ? "Maak er muziek van" : "Maak er een jingle van";
    public string KindTip => IsJingle
        ? "Dit is nu een jingle (gele streep). Zet het om naar muziek (blauw), in een muziekcategorie; een nieuwe scan houdt dat vast."
        : "Dit is nu muziek (blauwe streep). Zet het om naar een jingle (geel), in de categorie Jingles; een nieuwe scan houdt dat vast.";
    partial void OnCoverChanged(Avalonia.Media.Imaging.Bitmap? value) => OnPropertyChanged(nameof(ShowJingleCover));
    /// <summary>Position on PFL while this item is pre-listened; -1 otherwise.</summary>
    [ObservableProperty] private double _playhead = -1;

    /// <summary>A click in the waveform: pre-listen from there.</summary>
    [RelayCommand] private Task ListenAt(double seconds) => StudioHub.Current.Listen(Id, seconds);
    [ObservableProperty] private IReadOnlyList<WaveMarker> _markers = [];
    public ObservableCollection<string> History { get; } = [];
    [ObservableProperty] private string _historyText = "";

    public string Facts => string.Join("  ·  ", new[]
    {
        StudioHub.Time(Media.Duration),
        Media.Lufs is double l ? $"{l:0.0} LUFS" : null,
        Media.Bpm is double b ? $"{b:0.0} BPM" : null,
        Media.Camelot is { } k ? $"{k} ({Media.Key})" : null,
        Media.ReplayGain is double rg ? $"ReplayGain {rg:+0.0;-0.0} dB" : null,
        Media.Bitrate is int kbps ? $"{kbps} kbps" : null,
        Media.Category,
        Media.Kind == "jingle" ? "jingle" : null,
    }.Where(x => !string.IsNullOrEmpty(x)));

    public string MarkerText
    {
        get
        {
            var c = Media.Cues;
            var parts = new List<string> { $"in {StudioHub.TimeTenths(c.CueIn)}" };
            if (c.Intro is double i) parts.Add($"intro {StudioHub.TimeTenths(i)}");
            if (Media.EstIntro is double ei && c.Intro is null) parts.Add($"op niveau ±{StudioHub.Time(ei)}");
            parts.Add($"mix {StudioHub.TimeTenths(c.Mix)}");
            if (Media.EstOutro is double eo) parts.Add($"einde begint ±{StudioHub.Time(eo)}");
            parts.Add($"uit {StudioHub.TimeTenths(c.CueOut)}");
            if (Media.FirstBeat is double fb && Media.Bpm is double) parts.Add($"eerste tel {fb:0.00} s");
            return string.Join("  ·  ", parts) + (Media.CuesManual ? "  (met de hand nagekeken)" : "  (automatisch)");
        }
    }

    public string PlayText => Media.PlayCount == 0
        ? "Nog nooit uitgezonden"
        : $"{Media.PlayCount}× uitgezonden, laatst {Media.LastPlayed:d-M-yyyy HH:mm}";

    public string AddedText => Media.Added is DateTime a ? $"Toegevoegd {a:d-M-yyyy}" : "";
    public string ValidText => (Media.ValidFrom, Media.ValidUntil) switch
    {
        (null, null) => "",
        (DateTime f, null) => $"Geldig vanaf {f:d-M-yyyy}",
        (null, DateTime u) => $"Geldig tot {u:d-M-yyyy}",
        (DateTime f, DateTime u) => $"Geldig {f:d-M-yyyy} t/m {u:d-M-yyyy}",
    };
    public bool Blocked => !Media.Allowed;
    public string BlockText => Media.Block is { } b ? $"Nu niet: {b}" : "Mag nu ingepland worden";

    private async Task LoadAsync()
    {
        var hub = StudioHub.Current;
        var history = await hub.Client.GetAsync<List<PlayedDto>>($"/api/media/{Id}/history?limit=10");
        History.Clear();
        foreach (var h in history ?? [])
            History.Add($"{h.Started:ddd d-M HH:mm}" + (h.Played is double p ? $"  ({StudioHub.Time(p)})" : "") + (h.Source != "log" ? $"  {h.Source}" : ""));
        HistoryText = History.Count == 0 ? "Nog niet uitgezonden." : "";

        if (await hub.Client.GetAsync<PresenterTextDto>($"/api/media/{Id}/presenter-text") is { } pt) PresenterText = pt.Text;
        await LoadCoverAsync();
        var peaks = await hub.Client.GetAsync<PeaksDto>($"/api/media/{Id}/peaks");
        if (peaks is not null)
        {
            var bytes = Convert.FromBase64String(peaks.Data);
            var data = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, data, 0, data.Length * 2);
            Peaks = data;
        }
        var c = Media.Cues;
        var marks = new List<WaveMarker>
        {
            new("in", "in", c.CueIn, Avalonia.Media.Color.Parse("#3DBD7D")),
            new("mix", "mix", c.Mix, Avalonia.Media.Color.Parse("#F0B429")),
            new("out", "uit", c.CueOut, Avalonia.Media.Color.Parse("#E5484D")),
        };
        if (c.Intro is double i) marks.Add(new("intro", "intro", i, Avalonia.Media.Color.Parse("#3FA7FF")));
        Markers = marks;
    }

    [RelayCommand]
    private async Task SavePresenterText()
    {
        var r = await StudioHub.Current.Client.SendForAsync<PresenterTextDto>(HttpMethod.Put, $"/api/media/{Id}/presenter-text", new PresenterTextDto(PresenterText));
        PresenterSaveText = r is null ? "Opslaan mislukt." : r.Text.Length == 0 ? "Geen tekst meer bij dit item." : "Opgeslagen; staat groot in beeld zolang dit item speelt.";
        if (r is not null) PresenterText = r.Text;
    }

    [RelayCommand]
    private async Task SaveTags()
    {
        int? year = int.TryParse(Year.Trim(), out var y) && y is > 0 and < 3000 ? y : null;
        var r = await StudioHub.Current.Client.SendForAsync<MediaDto>(HttpMethod.Put, $"/api/media/{Id}/tags",
            new MediaTagsRequest(Artist, Title, Album, Genre, year, Language, Mood, Rating.Value, EnergyLevel.Value));
        SaveText = r is null ? "Opslaan mislukt." : "Opgeslagen.";
        if (r is not null) { Media = r; _owner.Refresh(); _ = _owner.LoadUndoAsync(); }
    }

    [RelayCommand] private void EditCues() => StudioHub.Current.RequestCueEditor(Id);

    /// <summary>Music becomes a jingle or the other way round; it moves to a category of that kind when needed.</summary>
    [RelayCommand]
    private async Task ToggleKind()
    {
        var r = await StudioHub.Current.Client.SendForAsync<MediaDto>(HttpMethod.Put, $"/api/media/{Id}/kind",
            new MediaKindRequest(IsJingle ? "song" : "jingle"));
        if (r is null) { SaveText = "Omzetten lukte niet."; return; }
        Media = r;
        foreach (var name in new[] { nameof(IsJingle), nameof(KindButtonText), nameof(KindTip), nameof(ShowJingleCover), nameof(Facts) })
            OnPropertyChanged(name);
        SaveText = r.Kind == "jingle" ? $"Nu een jingle, in {r.Category}." : $"Nu muziek, in {r.Category}.";
        _owner.Refresh();
        _ = _owner.LoadUndoAsync();
    }

    private sealed record WrittenDto(bool Written);

    /// <summary>Everything the library knows (tags, key, BPM, rights, ReplayGain) into the file itself.</summary>
    [RelayCommand]
    private async Task WriteAllTags()
    {
        var (r, error) = await StudioHub.Current.Client.SendWithErrorAsync<WrittenDto>(HttpMethod.Post, $"/api/media/{Id}/tags/write", null);
        SaveText = r is not null ? "Alle tags staan nu ook in het bestand." : error ?? "Schrijven lukte niet.";
    }

    /// <summary>Puts this picture into the file as its cover.</summary>
    public async Task SetCoverAsync(byte[] image)
    {
        var (r, error) = await StudioHub.Current.Client.PostBytesAsync<WrittenDto>($"/api/media/{Id}/cover", image);
        SaveText = r is not null ? "Hoes staat in het bestand." : error ?? "Hoes zetten lukte niet.";
        if (r is not null) await LoadCoverAsync();
    }

    [RelayCommand]
    private async Task RemoveCover()
    {
        var (r, error) = await StudioHub.Current.Client.SendWithErrorAsync<WrittenDto>(HttpMethod.Delete, $"/api/media/{Id}/cover", null);
        SaveText = r is not null ? "Afbeeldingen uit het bestand gehaald." : error ?? "Weghalen lukte niet.";
        if (r is not null) await LoadCoverAsync();
    }

    private async Task LoadCoverAsync()
    {
        Cover = null;
        if (await StudioHub.Current.Client.GetBytesQuietAsync($"/api/media/{Id}/cover?t={DateTime.Now.Ticks}") is { Length: > 0 } image)
        {
            try { Cover = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(new MemoryStream(image), 168); }
            catch (Exception) { Cover = null; }   // not an image Avalonia can read
        }
    }

    [RelayCommand]
    private async Task WriteMarkers()
    {
        bool ok = await StudioHub.Current.Client.PostAsync($"/api/media/{Id}/markers/write");
        SaveText = ok ? "Markers staan nu ook in het bestand." : "Schrijven in het bestand lukte niet.";
    }
}

public sealed record LevelChoice(int Value, string Name)
{
    public override string ToString() => Name;
}

public sealed record SortChoice(string Name, string Key)
{
    public override string ToString() => Name;
}

public sealed record ClickChoice(string Name, string Key)
{
    public override string ToString() => Name;
}
