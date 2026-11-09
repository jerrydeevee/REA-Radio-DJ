using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A campaign in the filter of the proof of broadcast (null id = all).</summary>
public sealed record AdCampaignChoice(long? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Commercials: campaigns (advertiser, run period, days and hours, plays per day and per hour, spots), ad breaks on
/// fixed times (opener, spots up to a maximum length, closer) and the proof of broadcast with a CSV export.
/// </summary>
public sealed partial class AdsTool : StudioTool
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public AdsTool() : base("ads", "Reclame") { }

    /// <summary>Status of a spot in the proof list.</summary>
    public static readonly Avalonia.Data.Converters.FuncValueConverter<AdPlayDto?, string> StatusText = new(p => p switch
    {
        null => "",
        { Complete: true } => "✓ volledig",
        { Started: not null, Played: null } => "speelt",
        { Started: not null } => "afgebroken",
        _ when p.Scheduled > DateTime.Now.AddMinutes(-10) => "gepland",
        _ => "niet uitgezonden",
    });

    public static readonly string[] PositionNames = ["Maakt niet uit", "Eerste in het blok", "Laatste in het blok"];
    public IReadOnlyList<string> Positions => PositionNames;
    public IReadOnlyList<string> Timings => NewsTool.TimingNames;

    // ------------------------------------------------------------------ campaigns

    public ObservableCollection<AdCampaignDto> Campaigns { get; } = [];
    public ObservableCollection<AdSpotDto> Spots { get; } = [];
    public ObservableCollection<DayChoice> CampaignDays { get; } = new(PlannerTool.DayNames.Select((n, i) => new DayChoice(i, n)));
    [ObservableProperty] private AdCampaignDto? _campaign;
    [ObservableProperty] private long _campaignId;
    [ObservableProperty] private string _advertiser = "";
    [ObservableProperty] private string _campaignName = "";
    [ObservableProperty] private bool _campaignEnabled = true;
    [ObservableProperty] private DateTime? _start = DateTime.Today;
    [ObservableProperty] private DateTime? _end = DateTime.Today.AddDays(27);
    [ObservableProperty] private string _campaignHours = "7-19";
    [ObservableProperty] private decimal? _perDay = 4;
    [ObservableProperty] private decimal? _perHour = 1;
    [ObservableProperty] private int _position;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private AdSpotDto? _spot;
    [ObservableProperty] private string? _campaignError;

    partial void OnCampaignChanged(AdCampaignDto? value)
    {
        if (value is null) return;
        CampaignId = value.Id;
        Advertiser = value.Advertiser;
        CampaignName = value.Name;
        CampaignEnabled = value.Enabled;
        Start = DateTime.TryParse(value.Start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s : DateTime.Today;
        End = DateTime.TryParse(value.End, CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : DateTime.Today;
        CampaignHours = value.Hours;
        PerDay = value.PerDay;
        PerHour = value.PerHour;
        Position = value.Position switch { "first" => 1, "last" => 2, _ => 0 };
        Notes = value.Notes;
        Spots.Clear();
        foreach (var x in value.Spots) Spots.Add(x);
        foreach (var d in CampaignDays) d.On = (value.Days & (1 << d.Index)) != 0;
        CampaignError = null;
    }

    [RelayCommand]
    private void NewCampaign()
    {
        Campaign = null;
        CampaignId = 0;
        Advertiser = "";
        CampaignName = "";
        CampaignEnabled = true;
        Start = DateTime.Today;
        End = DateTime.Today.AddDays(27);
        CampaignHours = "7-19";
        PerDay = 4;
        PerHour = 1;
        Position = 0;
        Notes = "";
        Spots.Clear();
        foreach (var d in CampaignDays) d.On = true;
        CampaignError = null;
    }

    [RelayCommand]
    private void AddSpot()
    {
        if (Hub.SelectedMedia is not { } m) { CampaignError = "Kies eerst de spot in de bibliotheek."; return; }
        if (Spots.Any(s => s.MediaId == m.Id)) return;
        Spots.Add(new AdSpotDto(m.Id, m.DisplayName, Math.Round(m.Cues.CueOut - m.Cues.CueIn, 1)));
        CampaignError = null;
    }

    [RelayCommand]
    private void RemoveSpot(AdSpotDto? spot)
    {
        if ((spot ?? Spot) is { } s) Spots.Remove(s);
    }

    [RelayCommand]
    private async Task SaveCampaign()
    {
        var d = new AdCampaignDto(CampaignId, Advertiser, CampaignName, CampaignEnabled, (Start ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (End ?? Start ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), CampaignDays.Where(x => x.On).Sum(x => 1 << x.Index),
            CampaignHours, (int)(PerDay ?? 4), (int)(PerHour ?? 1), Position switch { 1 => "first", 2 => "last", _ => "any" }, Spots.ToList(), Notes);
        if (string.IsNullOrWhiteSpace(Advertiser)) { CampaignError = "Vul een adverteerder in."; return; }
        var saved = await Hub.Client.SendForAsync<AdCampaignDto>(HttpMethod.Post, "/api/ads/campaigns", d);
        if (saved is null) { CampaignError = "Opslaan mislukt."; return; }
        await LoadCampaignsAsync(saved.Id);
        Hub.Status = $"campagne “{saved.Name}” opgeslagen";
    }

    [RelayCommand]
    private async Task DeleteCampaign()
    {
        if (CampaignId > 0) await Hub.Client.DeleteAsync($"/api/ads/campaigns/{CampaignId}");
        await LoadCampaignsAsync();
    }

    private async Task LoadCampaignsAsync(long? select = null)
    {
        if (await Hub.Client.GetAsync<List<AdCampaignDto>>("/api/ads/campaigns") is not { } list) return;
        long? keep = select ?? Campaign?.Id;
        Campaigns.Clear();
        foreach (var c in list) Campaigns.Add(c);
        Campaign = Campaigns.FirstOrDefault(c => c.Id == keep) ?? Campaigns.FirstOrDefault();
        if (Campaign is null) NewCampaign();
        ProofCampaigns.Clear();
        ProofCampaigns.Add(new AdCampaignChoice(null, "alle campagnes"));
        foreach (var c in list) ProofCampaigns.Add(new AdCampaignChoice(c.Id, $"{c.Advertiser} – {c.Name}"));
        ProofCampaign ??= ProofCampaigns[0];
    }

    // ------------------------------------------------------------------ breaks

    public ObservableCollection<AdBreakDto> Breaks { get; } = [];
    public ObservableCollection<NewsJingle> Jingles { get; } = [new(null, "geen")];
    public ObservableCollection<DayChoice> BreakDays { get; } = new(PlannerTool.DayNames.Select((n, i) => new DayChoice(i, n)));
    [ObservableProperty] private AdBreakDto? _break;
    [ObservableProperty] private long _breakId;
    [ObservableProperty] private string _breakName = "Reclame";
    [ObservableProperty] private bool _breakEnabled = true;
    [ObservableProperty] private string _breakHours = "7-19";
    [ObservableProperty] private decimal? _minute = 25;
    [ObservableProperty] private decimal? _second = 0;
    [ObservableProperty] private int _timing = 1;
    [ObservableProperty] private NewsJingle? _opener;
    [ObservableProperty] private NewsJingle? _closer;
    [ObservableProperty] private decimal? _maxSeconds = 120;
    [ObservableProperty] private string _previewText = "";

    partial void OnBreakChanged(AdBreakDto? value)
    {
        if (value is null) return;
        BreakId = value.Id;
        BreakName = value.Name;
        BreakEnabled = value.Enabled;
        BreakHours = value.Hours;
        Minute = value.Minute;
        Second = value.Second;
        Timing = value.Timing == "soft" ? 1 : 0;
        Opener = Jingles.FirstOrDefault(j => j.Id == value.OpenerId) ?? Jingles[0];
        Closer = Jingles.FirstOrDefault(j => j.Id == value.CloserId) ?? Jingles[0];
        MaxSeconds = value.MaxSeconds;
        foreach (var d in BreakDays) d.On = (value.Days & (1 << d.Index)) != 0;
        PreviewText = "";
    }

    [RelayCommand]
    private void NewBreak()
    {
        Break = null;
        BreakId = 0;
        BreakName = "Reclame";
        BreakEnabled = true;
        BreakHours = "7-19";
        Minute = 25;
        Second = 0;
        Timing = 1;
        Opener = Jingles[0];
        Closer = Jingles[0];
        MaxSeconds = 120;
        foreach (var d in BreakDays) d.On = true;
        PreviewText = "";
    }

    [RelayCommand]
    private async Task SaveBreak()
    {
        var d = new AdBreakDto(BreakId, BreakName, BreakEnabled, BreakDays.Where(x => x.On).Sum(x => 1 << x.Index), BreakHours, (int)(Minute ?? 0),
            (int)(Second ?? 0), Timing == 1 ? "soft" : "hard", Opener?.Id, Closer?.Id, (int)(MaxSeconds ?? 120));
        var saved = await Hub.Client.SendForAsync<AdBreakDto>(HttpMethod.Post, "/api/ads/breaks", d);
        if (saved is null) { PreviewText = "Opslaan mislukt."; return; }
        await LoadBreaksAsync(saved.Id);
        Hub.Status = $"reclameblok “{saved.Name}” opgeslagen";
    }

    [RelayCommand]
    private async Task DeleteBreak()
    {
        if (BreakId > 0) await Hub.Client.DeleteAsync($"/api/ads/breaks/{BreakId}");
        await LoadBreaksAsync();
    }

    [RelayCommand]
    private async Task PreviewBreak()
    {
        if (BreakId <= 0) { PreviewText = "Sla het blok eerst op."; return; }
        var p = await Hub.Client.GetAsync<AdPreviewDto>($"/api/ads/breaks/{BreakId}/preview");
        if (p is null) { PreviewText = "Engine niet bereikbaar."; return; }
        PreviewText = p.Problem is { } problem
            ? (Break?.Next is DateTime n ? $"Om {n:HH:mm}: " : "") + problem
            : (Break?.Next is DateTime t ? $"Om {t:HH:mm} ({p.Seconds:0} s spots):\n" : "") + string.Join("\n", p.Lines.Select(l => "• " + l));
    }

    private async Task LoadBreaksAsync(long? select = null)
    {
        if (await Hub.Client.GetAsync<List<AdBreakDto>>("/api/ads/breaks") is not { } list) return;
        long? keep = select ?? Break?.Id;
        Breaks.Clear();
        foreach (var b in list) Breaks.Add(b);
        Break = Breaks.FirstOrDefault(b => b.Id == keep) ?? Breaks.FirstOrDefault();
        if (Break is null) NewBreak();
    }

    // ------------------------------------------------------------------ proof of broadcast

    public ObservableCollection<AdPlayDto> Plays { get; } = [];
    public ObservableCollection<AdCampaignChoice> ProofCampaigns { get; } = [];
    [ObservableProperty] private AdCampaignChoice? _proofCampaign;
    [ObservableProperty] private DateTime? _proofFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime? _proofTo = DateTime.Today;
    [ObservableProperty] private string _proofSummary = "";

    private string ProofQuery => $"from={ProofFrom ?? DateTime.Today:yyyy-MM-dd}&to={ProofTo ?? DateTime.Today:yyyy-MM-dd}"
        + (ProofCampaign?.Id is long id ? $"&campaign={id}" : "");
    public string ProofUrl => "/api/ads/proof.csv?" + ProofQuery;
    public string ProofFileName => $"uitzendbewijs-{ProofFrom ?? DateTime.Today:yyyy-MM-dd}-{ProofTo ?? DateTime.Today:yyyy-MM-dd}.csv";

    [RelayCommand]
    private async Task LoadProof()
    {
        if (await Hub.Client.GetAsync<List<AdPlayDto>>("/api/ads/plays?" + ProofQuery) is not { } list) return;
        Plays.Clear();
        foreach (var p in list.OrderByDescending(p => p.Scheduled)) Plays.Add(p);
        int aired = list.Count(p => p.Started is not null);
        int complete = list.Count(p => p.Complete);
        int waiting = list.Count(p => p.Started is null && p.Scheduled > DateTime.Now.AddMinutes(-10));
        ProofSummary = string.Format(Nl, "{0} uitgezonden, waarvan {1} volledig{2}", aired, complete,
            waiting > 0 ? $" · {waiting} gepland in een komend blok" : "");
    }

    partial void OnProofCampaignChanged(AdCampaignChoice? value) => _ = LoadProof();

    protected override void OnFirstShow() => _ = LoadAllAsync();

    private async Task LoadAllAsync()
    {
        if (await Hub.Client.GetAsync<List<MediaDto>>("/api/library?kind=jingle&limit=1000") is { } jingles)
            foreach (var m in jingles.Where(m => m.Enabled).OrderBy(m => m.DisplayName)) Jingles.Add(new NewsJingle(m.Id, m.DisplayName));
        await LoadCampaignsAsync();
        await LoadBreaksAsync();
        await LoadProof();
    }
}
