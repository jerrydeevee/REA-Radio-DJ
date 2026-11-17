using ReaRadioDj.Contracts.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A metadata proposal with a tick box.</summary>
public sealed partial class MetaRow(MetaProposalDto p) : ObservableObject
{
    public MetaProposalDto P { get; } = p;
    [ObservableProperty] private bool _chosen = true;
    public string Adds => string.Join(" · ", new[]
    {
        P.NewArtist is null ? null : Tr.F("artiest: {0}", P.NewArtist),
        P.NewTitle is null ? null : Tr.F("titel: {0}", P.NewTitle),
        P.Album is null ? null : Tr.F("album: {0}", P.Album),
        P.Year is null ? null : Tr.F("jaar: {0}", P.Year),
        P.Isrc is null ? null : $"ISRC: {P.Isrc}",
        P.Label is null ? null : Tr.F("label: {0}", P.Label),
        P.Genre is null ? null : Tr.F("genre: {0}", P.Genre),
        P.Composer is null ? null : Tr.F("componist/auteur: {0}", P.Composer),
        P.CoverUrl is null ? null : "hoes",
    }.Where(x => x is not null));
    public string From => $"{P.Source}: {P.Found}";
}

/// <summary>
/// Data sources for the library: music catalogues in other databases (ODBC), read in on request or every few minutes,
/// and a lookup of missing album, year, ISRC, label, genre, composers/authors and cover in MusicBrainz and the Cover Art
/// Archive (and Spotify, when its keys are set).
/// </summary>
public sealed partial class SourcesTool : StudioTool
{
    public SourcesTool() : base("sources", "Databronnen") { }

    public static readonly string[] KindNames = ["Muziek", "Jingles"];
    public IReadOnlyList<string> Kinds => KindNames;

    public ObservableCollection<DbSourceDto> Sources { get; } = [];
    public ObservableCollection<string> TestLines { get; } = [];

    [ObservableProperty] private DbSourceDto? _selected;
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _connection = "";
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _kind;
    [ObservableProperty] private decimal? _syncMinutes = 0;
    [ObservableProperty] private string _pathFrom = "";
    [ObservableProperty] private string _pathTo = "";
    [ObservableProperty] private string _lastResult = "";
    [ObservableProperty] private string? _problem;
    [ObservableProperty] private bool _busy;

    partial void OnSelectedChanged(DbSourceDto? value)
    {
        if (value is null) return;
        Id = value.Id;
        Name = value.Name;
        Enabled = value.Enabled;
        Connection = value.Connection;
        Query = value.Query;
        Kind = value.Kind == "jingle" ? 1 : 0;
        SyncMinutes = value.SyncMinutes;
        PathFrom = value.PathFrom;
        PathTo = value.PathTo;
        Busy = value.Busy;
        LastResult = value.Busy ? Tr.T("bezig met inlezen…")
            : value.LastSync is DateTime t ? Tr.F("Laatst ingelezen {0:ddd d MMM HH:mm}:\n{1}", t, value.LastResult) : Tr.T("Nog niet ingelezen.");
        TestLines.Clear();
        Problem = null;
    }

    protected override void OnFirstShow()
    {
        _ = LoadAsync();
        _ = PollMetaAsync();
    }

    private async Task LoadAsync(long? select = null)
    {
        if (await Hub.Client.GetAsync<List<DbSourceDto>>("/api/sources") is not { } list) return;
        long? keep = select ?? Selected?.Id;
        Sources.Clear();
        foreach (var s in list) Sources.Add(s);
        Selected = Sources.FirstOrDefault(s => s.Id == keep) ?? Sources.FirstOrDefault();
        if (Selected is null) New();
    }

    private DbSourceDto Current() => new(Id, Name, Enabled, Connection, Query, Kind == 1 ? "jingle" : "song", (int)(SyncMinutes ?? 0), PathFrom, PathTo);

    [RelayCommand]
    private void New()
    {
        Selected = null;
        Id = 0;
        Name = "Muziekdatabase";
        Enabled = true;
        Connection = "";
        Query = Tr.T("SELECT bestand AS path, artiest AS artist, titel AS title, categorie AS category FROM nummers");
        Kind = 0;
        SyncMinutes = 0;
        PathFrom = "";
        PathTo = "";
        LastResult = Tr.T("Nog niet ingelezen.");
        TestLines.Clear();
        Problem = null;
    }

    [RelayCommand]
    private async Task Save()
    {
        var saved = await Hub.Client.SendForAsync<DbSourceDto>(HttpMethod.Post, "/api/sources", Current());
        if (saved is null) { Problem = Tr.T("Opslaan mislukt."); return; }
        await LoadAsync(saved.Id);
        Hub.Status = Tr.F("databron “{0}” opgeslagen", saved.Name);
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Id <= 0) { New(); return; }
        await Hub.Client.DeleteAsync($"/api/sources/{Id}");
        Selected = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Test()
    {
        TestLines.Clear();
        Problem = null;
        LastResult = Tr.T("bezig met testen…");
        var r = await Hub.Client.SendForAsync<DbTestDto>(HttpMethod.Post, "/api/sources/test", Current());
        if (r is null) { LastResult = ""; Problem = Tr.T("Test mislukt: engine niet bereikbaar?"); return; }
        LastResult = r.Columns.Count == 0 ? "" : Tr.T("Kolommen: ") + string.Join(", ", r.Columns)
            + (r.Used.Count > 0 ? "\nGebruikt: " + string.Join(", ", r.Used) : "")
            + (r.Sample.Count > 0 ? Tr.F("\nEerste {0} rijen:", r.Sample.Count) : "");
        foreach (var l in r.Sample) TestLines.Add(l);
        Problem = r.Problem;
    }

    [RelayCommand]
    private async Task Sync()
    {
        if (Id <= 0) await Save();
        if (Id <= 0) return;
        if (!await Hub.Client.PostAsync($"/api/sources/{Id}/sync")) { Problem = Tr.T("Al bezig, of de bron is niet opgeslagen."); return; }
        Busy = true;
        LastResult = Tr.T("bezig met inlezen…");
        // Follow the sync until it is done.
        for (int i = 0; i < 3600 && Busy; i++)
        {
            await Task.Delay(1000);
            if (await Hub.Client.GetAsync<List<DbSourceDto>>("/api/sources") is { } list && list.FirstOrDefault(s => s.Id == Id) is { } s && !s.Busy)
            {
                await LoadAsync(Id);
                Hub.Status = Tr.F("databron “{0}” ingelezen", s.Name);
            }
        }
    }

    // ---------------------------------------------------------------- metadata

    public ObservableCollection<MetaRow> Proposals { get; } = [];
    [ObservableProperty] private string _metaStatus = "";
    [ObservableProperty] private bool _metaRunning;
    [ObservableProperty] private decimal? _metaLimit = 50;
    [ObservableProperty] private string _spotifyNote = "";
    [ObservableProperty] private bool _metaAuto;
    [ObservableProperty] private string _metaAutoNote = "";
    private bool _metaAutoLoading;

    partial void OnMetaAutoChanged(bool value)
    {
        if (_metaAutoLoading) return;
        _ = Hub.Client.SendForAsync<MetaAutoDto>(HttpMethod.Put, "/api/metadata/auto", new MetaAutoDto(value));
    }

    private async Task PollMetaAsync()
    {
        while (true)
        {
            await RefreshMetaAsync();
            await Task.Delay(MetaRunning ? 1000 : 5000);
        }
    }

    private async Task RefreshMetaAsync()
    {
        if (await Hub.Client.GetAsync<MetaStatusDto>("/api/metadata/status") is not { } s) return;
        MetaRunning = s.Running;
        SpotifyNote = (s.Spotify ? Tr.T("MusicBrainz, en Spotify als aanvulling") : Tr.T("MusicBrainz (Spotify doet mee zodra de Spotify-sleutels zijn ingevuld)"))
            + (s.AcoustId ? Tr.T("; niet gevonden nummers worden op geluid herkend (AcoustID).") : Tr.T("; herkennen op geluid kan met een AcoustID-sleutel (Instellingen → AcoustID)."));
        _metaAutoLoading = true;
        MetaAuto = s.AutoOnPlay;
        _metaAutoLoading = false;
        MetaAutoNote = s.AutoDone > 0 ? Tr.F("Sinds de start {0} nummers aangevuld bij eerste keer draaien; laatste: {1}", s.AutoDone, s.AutoLast) : "";
        MetaStatus = s.Running
            ? Tr.F("Bezig: {0} van {1} · {2}", s.Done, s.Total, s.Current)
            : s.Total > 0 ? Tr.F("Klaar: {0} nummers bekeken · {1} met aanvullingen · {2} al compleet · {3} niet gevonden", s.Total, s.Proposals.Count, s.NothingNew, s.NotFound) : "";
        // Keep the ticks the user set; add new proposals, drop the ones that are gone.
        var ids = s.Proposals.Select(p => p.MediaId).ToHashSet();
        foreach (var gone in Proposals.Where(r => !ids.Contains(r.P.MediaId)).ToList()) Proposals.Remove(gone);
        var have = Proposals.Select(r => r.P.MediaId).ToHashSet();
        foreach (var p in s.Proposals.Where(p => !have.Contains(p.MediaId))) Proposals.Add(new MetaRow(p));
    }

    [RelayCommand]
    private async Task StartMeta()
    {
        await Hub.Client.PostAsync("/api/metadata/start", new MetaStartDto((int)(MetaLimit ?? 50)));
        await RefreshMetaAsync();
    }

    [RelayCommand] private Task StopMeta() => Hub.Client.PostAsync("/api/metadata/stop");

    [RelayCommand]
    private async Task ApplyMeta()
    {
        var ids = Proposals.Where(r => r.Chosen).Select(r => r.P.MediaId).ToList();
        if (ids.Count == 0) return;
        await Hub.Client.PostAsync("/api/metadata/apply", new IdListDto(ids));
        Hub.Status = Tr.F("{0} nummers aangevuld", ids.Count);
        await RefreshMetaAsync();
    }

    [RelayCommand]
    private async Task DismissMeta()
    {
        var ids = Proposals.Where(r => r.Chosen).Select(r => r.P.MediaId).ToList();
        if (ids.Count == 0) return;
        await Hub.Client.PostAsync("/api/metadata/dismiss", new IdListDto(ids));
        await RefreshMetaAsync();
    }

    [RelayCommand] private void ChooseAll() { foreach (var r in Proposals) r.Chosen = true; }
    [RelayCommand] private void ChooseNone() { foreach (var r in Proposals) r.Chosen = false; }
}
