using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>The Recordings window, part three: "uitzending gemist" / podcast per programme.</summary>
public sealed partial class RecordingsTool
{
    public ObservableCollection<PodcastVm> Podcasts { get; } = [];
    [ObservableProperty] private string _podcastStatus = "";
    [ObservableProperty] private string _podcastFolder = "";
    [ObservableProperty] private bool _programRecordingOff;

    [RelayCommand]
    private async Task LoadPodcasts()
    {
        var d = await Hub.Client.GetAsync<PodcastsDto>("/api/podcasts");
        if (d is null) { PodcastStatus = Tr.T("De engine kent uitzending gemist nog niet: werk hem bij."); return; }
        ApplyPodcasts(d);
    }

    private void ApplyPodcasts(PodcastsDto d)
    {
        Podcasts.Clear();
        foreach (var p in d.Items) Podcasts.Add(new PodcastVm(this, p));
        PodcastFolder = d.Folder;
        ProgramRecordingOff = !d.ProgramRecording;
    }

    [RelayCommand]
    private void AddPodcast() => Podcasts.Add(new PodcastVm(this, new PodcastStatusDto(
        new PodcastDto("", Tr.T("Ochtendshow"), true, 0b001_1111, "07:00", "09:00", "", "", 10, "", ""), null, 0, null, "", null)));

    [RelayCommand]
    private async Task SavePodcasts()
    {
        var (d, error) = await Hub.Client.SendWithErrorAsync<PodcastsDto>(HttpMethod.Put, "/api/podcasts", Podcasts.Select(p => p.ToDto()).ToList());
        if (d is null) { PodcastStatus = Tr.F("Niet gelukt: {0}", error); return; }
        ApplyPodcasts(d);
        PodcastStatus = Tr.T("Opgeslagen.");
    }

    internal void RemovePodcast(PodcastVm p) => Podcasts.Remove(p);

    internal async Task PublishNow(PodcastVm p)
    {
        if (string.IsNullOrEmpty(p.Id)) { PodcastStatus = Tr.T("Sla het programma eerst op."); return; }
        PodcastStatus = Tr.F("De laatste uitzending van {0} wordt geknipt …", p.Name);
        var (d, error) = await Hub.Client.SendWithErrorAsync<PodcastsDto>(HttpMethod.Post, $"/api/podcasts/{p.Id}/publish", null);
        if (d is not null) ApplyPodcasts(d);
        PodcastStatus = error is null ? Tr.T("Klaar.") : Tr.F("Niet gelukt: {0}", error);
    }
}

public sealed partial class PodcastVm : ObservableObject
{
    private readonly RecordingsTool _owner;

    public PodcastVm(RecordingsTool owner, PodcastStatusDto s)
    {
        _owner = owner;
        var d = s.Item;
        Id = d.Id;
        _name = d.Name; _enabled = d.Enabled; _start = d.Start; _end = d.End; _description = d.Description; _author = d.Author;
        _keep = d.Keep; _copyFolder = d.CopyFolder; _publicUrl = d.PublicUrl;
        foreach (var day in Days) day.On = (d.Days & (1 << day.Index)) != 0;
        FeedUrl = s.FeedUrl;
        var parts = new List<string>();
        if (s.Problem is { } p) parts.Add(Tr.F("Klopt nog niet: {0}", Tr.E(p)));
        if (Id.Length > 0) parts.Add(s.Newest is DateTime n ? Tr.F("{0} afleveringen, nieuwste van {1:ddd d MMM HH:mm}", s.Episodes, n) : Tr.T("Nog geen afleveringen"));
        if (s.LastResult is { Length: > 22 } r && r.StartsWith("fout ", StringComparison.Ordinal)) parts.Add(Tr.F("Laatste poging mislukt: {0}", Tr.E(r[22..])));
        StatusText = string.Join("  ·  ", parts);
    }

    public string Id { get; }
    public string FeedUrl { get; }
    public bool HasFeed => Id.Length > 0;
    public string StatusText { get; }
    public ObservableCollection<DayChoice> Days { get; } = new(PlannerTool.DayNames.Select((n, i) => new DayChoice(i, n)));

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _start;
    [ObservableProperty] private string _end;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _author;
    [ObservableProperty] private decimal? _keep;
    [ObservableProperty] private string _copyFolder;
    [ObservableProperty] private string _publicUrl;

    public PodcastDto ToDto() => new(Id, Name, Enabled, Days.Where(d => d.On).Sum(d => 1 << d.Index), Start, End, Description, Author,
        (int)(Keep ?? 10), CopyFolder, PublicUrl);

    [RelayCommand] private void Remove() => _owner.RemovePodcast(this);
    [RelayCommand] private Task Publish() => _owner.PublishNow(this);
}
