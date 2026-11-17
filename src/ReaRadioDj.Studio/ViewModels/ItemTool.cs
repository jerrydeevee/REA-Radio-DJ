using ReaRadioDj.Contracts.Localization;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed record ItemField(string Name, string Value);

/// <summary>
/// Itemgegevens: everything known about one item, either the item ON AIR (follows the log) or the item selected in the
/// library: title, artist, album, year, category, length, tempo, key, loudness, intro, plays and the file.
/// </summary>
public sealed partial class ItemTool : StudioTool
{
    private long? _shown;

    public ItemTool() : base("item", "Itemgegevens") { }

    /// <summary>True = the item ON AIR; false = the item selected in the library.</summary>
    [ObservableProperty] private bool _followOnAir = true;
    [ObservableProperty] private string _heading = Tr.T("Nog geen item");
    [ObservableProperty] private string _sub = "";
    [ObservableProperty] private string _kind = "";
    public ObservableCollection<ItemField> Fields { get; } = [];
    public bool HasItem => Fields.Count > 0;

    protected override void OnFirstShow()
    {
        Hub.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StudioHub.SelectedMedia) && !FollowOnAir && Hub.SelectedMedia is { } m) Show(m);
        };
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Follow(string? what) => FollowOnAir = what != "selection";

    partial void OnFollowOnAirChanged(bool value)
    {
        _shown = null;
        if (!value && Hub.SelectedMedia is { } m) Show(m);
        else if (value && Hub.State is { } s) OnState(s);
    }

    protected override void OnState(StateDto s)
    {
        if (!FollowOnAir) return;
        var c = s.Engine.Current;
        if (c is null || c.MediaId <= 0)
        {
            if (_shown is not null || Fields.Count == 0) { _shown = null; Clear(c?.Label ?? Tr.T("Niets ON AIR")); }
            return;
        }
        if (_shown == c.MediaId) return;
        _shown = c.MediaId;
        _ = LoadAsync(c.MediaId);
    }

    private async Task LoadAsync(long id)
    {
        var m = await Hub.Client.GetAsync<MediaDto>($"/api/media/{id}");
        if (m is not null && _shown == id) Show(m);
    }

    private void Clear(string heading)
    {
        Heading = heading;
        Sub = "";
        Kind = "";
        Fields.Clear();
        OnPropertyChanged(nameof(HasItem));
    }

    private void Show(MediaDto m)
    {
        _shown = m.Id;
        Heading = string.IsNullOrWhiteSpace(m.Title) ? Path.GetFileNameWithoutExtension(m.Path) : m.Title;
        Sub = string.Join(" · ", new[] { m.Artist, m.Album }.Where(t => !string.IsNullOrWhiteSpace(t)));
        Kind = m.Kind;
        var nl = Contracts.Localization.Tr.Culture;
        var c = m.Cues;
        var rows = new List<ItemField>
        {
            new(Tr.T("Categorie"), m.Category),
            new(Tr.T("Jaar"), m.Year?.ToString(nl) ?? ""),
            new(Tr.T("Genre"), m.Genre),
            new(Tr.T("Lengte"), StudioHub.Time(c.CueOut - c.CueIn)),
            new(Tr.T("Intro"), c.Intro is double i && i > c.CueIn ? StudioHub.TimeTenths(i - c.CueIn) : m.EstIntro is double e ? $"± {StudioHub.TimeTenths(e)}" : ""),
            new(Tr.T("Mixpunt"), StudioHub.Time(c.Mix - c.CueIn)),
            new(Tr.T("Tempo"), m.Bpm is double b ? $"{b.ToString("0.#", nl)} BPM" : ""),
            new(Tr.T("Toonsoort"), string.Join(" · ", new[] { m.Key, m.Camelot }.Where(t => !string.IsNullOrWhiteSpace(t)))),
            new(Tr.T("Luidheid"), m.Lufs is double l ? $"{l.ToString("0.0", nl)} LUFS" : ""),
            new(Tr.T("Energie"), m.EnergyLevel is int en ? $"{en} / 10" : ""),
            new(Tr.T("Stemming"), m.Mood),
            new(Tr.T("Taal"), m.Language),
            new(Tr.T("Gespeeld"), m.PlayCount == 0 ? Tr.T("nog nooit") : $"{m.PlayCount}×" + (m.LastPlayed is DateTime lp ? Tr.F(", laatst {0}", lp.ToLocalTime().ToString("d MMM yyyy HH:mm", nl)) : "")),
            new(Tr.T("Toegevoegd"), m.Added?.ToLocalTime().ToString("d MMM yyyy", nl) ?? ""),
            new(Tr.T("Geldig"), m.ValidFrom is null && m.ValidUntil is null ? "" :
                $"{m.ValidFrom?.ToString("d MMM yyyy", nl) ?? "…"} t/m {m.ValidUntil?.ToString("d MMM yyyy", nl) ?? "…"}"),
            new(Tr.T("Kwaliteit"), m.Bitrate is int br ? Tr.F("{0} kbit/s", br) : ""),
            new(Tr.T("Let op"), m.Missing ? Tr.T("bestand niet gevonden") : m.Issues is { Count: > 0 } iss ? string.Join(", ", iss.Select(Tr.E)) : ""),
            new(Tr.T("Bestand"), m.Path),
        };
        Fields.Clear();
        foreach (var r in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value))) Fields.Add(r);
        OnPropertyChanged(nameof(HasItem));
    }
}
