using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Placeholders: items whose sound is still to come (an ad that is delivered later), already planned in a campaign,
/// a clock or a cart wall. The engine warns a day before the sound must be there and when it is late.
/// </summary>
public sealed partial class PlaceholdersTool : StudioTool
{
    public PlaceholdersTool() : base("placeholders", Tr.T("Plaatshouders")) { }

    private static readonly string[] TimeFormats = [@"h\:mm", @"hh\:mm"];

    public ObservableCollection<PlaceholderVm> Items { get; } = [];
    public ObservableCollection<PlaceholderCampaignDto> Campaigns { get; } = [];

    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private decimal? _seconds = 30;
    [ObservableProperty] private DateTime? _dueDay = DateTime.Today.AddDays(1);
    [ObservableProperty] private string _dueTime = "12:00";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private PlaceholderCampaignDto? _campaign;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _folder = "";

    protected override void OnFirstShow() => _ = Load();

    [RelayCommand]
    private async Task Load()
    {
        var d = await Hub.Client.GetAsync<PlaceholdersDto>("/api/placeholders");
        if (d is null) { Status = Tr.T("De engine kent plaatshouders nog niet: werk hem bij."); return; }
        Apply(d);
    }

    private void Apply(PlaceholdersDto d)
    {
        Items.Clear();
        foreach (var p in d.Items) Items.Add(new PlaceholderVm(this, p));
        var keep = Campaign?.Id;
        Campaigns.Clear();
        Campaigns.Add(new PlaceholderCampaignDto(0, Tr.T("(geen campagne)")));
        foreach (var c in d.Campaigns) Campaigns.Add(c);
        Campaign = Campaigns.FirstOrDefault(c => c.Id == keep) ?? Campaigns[0];
        Folder = d.Folder;
    }

    [RelayCommand]
    private async Task Add()
    {
        if (Title.Trim().Length == 0) { Status = Tr.T("Geef een titel."); return; }
        if (!TimeSpan.TryParseExact(DueTime.Trim(), TimeFormats, CultureInfo.InvariantCulture, out var t) || t >= TimeSpan.FromDays(1))
        { Status = Tr.T("Tijd: geef een tijd als 12:00."); return; }
        var due = (DueDay ?? DateTime.Today).Date + t;
        var (d, error) = await Hub.Client.SendWithErrorAsync<PlaceholdersDto>(HttpMethod.Post, "/api/placeholders",
            new PlaceholderRequest(Artist, Title, (double)(Seconds ?? 30), due, Note, CampaignId: Campaign is { Id: > 0 } c ? c.Id : null));
        if (d is null) { Status = Tr.F("Niet gelukt: {0}", error); return; }
        Apply(d);
        Status = Tr.F("Plaatshouder gemaakt: {0}", Title);
        Title = "";
        Note = "";
    }

    internal async Task Delete(PlaceholderVm p)
    {
        var (d, error) = await Hub.Client.SendWithErrorAsync<PlaceholdersDto>(HttpMethod.Delete, $"/api/placeholders/{p.Id}", null);
        if (d is not null) Apply(d);
        Status = error is null ? "" : Tr.F("Niet gelukt: {0}", error);
    }

    /// <summary>Sends the chosen sound file to the engine, which puts it in place of the placeholder.</summary>
    public async Task Deliver(PlaceholderVm p, string fileName, byte[] data)
    {
        Status = Tr.F("Geluid versturen: {0} …", fileName);
        var (d, error) = await Hub.Client.PostBytesAsync<PlaceholdersDto>($"/api/placeholders/{p.Id}/audio?name={Uri.EscapeDataString(fileName)}", data);
        if (d is not null) Apply(d);
        Status = error is null ? Tr.F("Het geluid van {0} is binnen.", p.Name) : Tr.F("Niet gelukt: {0}", error);
    }
}

public sealed partial class PlaceholderVm(PlaceholdersTool owner, PlaceholderDto d) : ObservableObject
{
    public long Id { get; } = d.Id;
    public string Name { get; } = d.Artist.Length > 0 ? $"{d.Artist} – {d.Title}" : d.Title;
    public bool Waiting => !d.Arrived;
    public bool Late => d.Late;

    public string StateText => d.Arrived ? Tr.T("Geluid is binnen")
        : d.Late ? Tr.F("TE LAAT: moest er {0:ddd d MMM HH:mm} zijn", d.Due)
        : d.Soon ? Tr.F("Moet er {0:ddd d MMM HH:mm} zijn: binnenkort!", d.Due)
        : Tr.F("Moet er {0:ddd d MMM HH:mm} zijn", d.Due);

    public string Details => string.Join("  ·  ", new[]
    {
        $"{d.Seconds:0} s",
        Tr.F("Bestandsnaam: {0}", d.FileName),
        d.Campaigns.Count > 0 ? Tr.F("In campagne: {0}", string.Join(", ", d.Campaigns)) : "",
        d.Note,
    }.Where(x => x.Length > 0));

    public PlaceholdersTool Owner { get; } = owner;

    [RelayCommand] private Task Delete() => Owner.Delete(this);
}
