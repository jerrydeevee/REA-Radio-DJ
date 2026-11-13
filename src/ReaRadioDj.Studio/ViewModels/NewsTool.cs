using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>A jingle for the opener or closer (null = none).</summary>
public sealed record NewsJingle(long? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// News management: news blocks on fixed times with an opener, the news itself (the newest file in a folder, an item
/// from a category, RSS headlines read out by the AI voice, an AI bulletin from national and regional feeds, or a
/// weather report) and a closer. Test shows what a block would play now; Proef writes it, Proef met stem speaks it in PFL.
/// </summary>
public sealed partial class NewsTool : StudioTool
{
    public NewsTool() : base("news", "Nieuws") { }

    public static readonly string[] SourceNames = ["Nieuwste bestand in een map", "Item uit een categorie", "Koppen uit RSS, letterlijk voorgelezen door de AI-stem",
        "AI-nieuws: landelijk en regionaal, herschreven door het taalmodel", "Weerbericht voor een plaats (AI)"];
    private static readonly string[] SourceKeys = ["folder", "category", "headlines", "ainews", "weather"];
    public static readonly string[] TimingNames = ["Hard (op de seconde, onderbreekt)", "Zacht (na het lopende item)"];
    public IReadOnlyList<string> Sources => SourceNames;
    public IReadOnlyList<string> Timings => TimingNames;

    public ObservableCollection<NewsBlockDto> Blocks { get; } = [];
    public ObservableCollection<NewsJingle> Jingles { get; } = [new(null, "geen")];
    public ObservableCollection<CategoryDto> Categories { get; } = [];
    public ObservableCollection<DayChoice> Days { get; } = new(PlannerTool.DayNames.Select((n, i) => new DayChoice(i, n)));

    [ObservableProperty] private NewsBlockDto? _selected;
    [ObservableProperty] private long _id;
    [ObservableProperty] private string _name = "Nieuws";
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _hours = "*";
    [ObservableProperty] private decimal? _minute = 0;
    [ObservableProperty] private decimal? _second = 0;
    [ObservableProperty] private int _timing;
    [ObservableProperty] private NewsJingle? _opener;
    [ObservableProperty] private NewsJingle? _closer;
    [ObservableProperty] private int _source;
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private decimal? _maxAge = 60;
    [ObservableProperty] private CategoryDto? _category;
    [ObservableProperty] private string _feeds = "";
    [ObservableProperty] private decimal? _maxHeadlines = 5;
    [ObservableProperty] private string _checkText = "";
    [ObservableProperty] private string? _checkProblem;
    [ObservableProperty] private string? _script;

    // AI news and weather
    [ObservableProperty] private string _regionalFeeds = "";
    [ObservableProperty] private decimal? _regionalCount = 3;
    [ObservableProperty] private bool _regionalFirst;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LengthText))] private decimal? _words = 120;
    [ObservableProperty] private bool _attribution = true;
    [ObservableProperty] private decimal? _maxAgeHours = 6;
    [ObservableProperty] private NewsJingle? _bed;
    [ObservableProperty] private NewsJingle? _voice;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlaceText))] private string _place = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlaceText))] private double _latitude;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlaceText))] private double _longitude;
    [ObservableProperty] private string _placeQuery = "";
    [ObservableProperty] private PlaceDto? _placeChoice;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _busy;
    public ObservableCollection<PlaceDto> Places { get; } = [];
    public bool HasPlaces => Places.Count > 0;
    public ObservableCollection<NewsJingle> Voices { get; } = [new(null, "stem van de rol: nieuwslezer of weerman/weervrouw (venster Stemmen)")];

    public string LengthText => $"woorden, ongeveer {Math.Round((double)(Words ?? 120) / 2.5 / 5) * 5:0} seconden";
    public string PlaceText => Latitude == 0 && Longitude == 0
        ? "Geen plaats gekozen: dan geldt de plaats van de AI-presentator."
        : string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Weer voor {(Place.Length > 0 ? Place : "plaats")} ({Latitude:0.###}, {Longitude:0.###})");

    partial void OnPlaceChoiceChanged(PlaceDto? value)
    {
        if (value is null) return;
        Place = value.Name;
        Latitude = value.Latitude;
        Longitude = value.Longitude;
    }

    public bool IsFolder => Source == 0;
    public bool IsCategory => Source == 1;
    public bool IsHeadlines => Source == 2;
    public bool IsAiNews => Source == 3;
    public bool IsWeather => Source == 4;
    public bool HasFeeds => Source is 2 or 3;
    public bool IsAi => Source is 3 or 4;
    public string FeedsLabel => Source == 3 ? "Landelijke bronnen (bijv. de NOS), één per regel: adres | naam" : "Bronnen (RSS/Atom), één per regel: adres | naam";
    public bool HasSelection => Selected is not null || Id == 0;

    partial void OnSourceChanged(int value)
    {
        foreach (var p in new[] { nameof(IsFolder), nameof(IsCategory), nameof(IsHeadlines), nameof(IsAiNews), nameof(IsWeather),
                     nameof(HasFeeds), nameof(IsAi), nameof(FeedsLabel) })
            OnPropertyChanged(p);
        ClearCheck();
    }

    private void ClearCheck() { CheckText = ""; CheckProblem = null; Script = null; Note = null; }

    partial void OnSelectedChanged(NewsBlockDto? value)
    {
        if (value is null) return;
        Id = value.Id;
        Name = value.Name;
        Enabled = value.Enabled;
        Hours = value.Hours;
        Minute = value.Minute;
        Second = value.Second;
        Timing = value.Timing == "soft" ? 1 : 0;
        Opener = Jingles.FirstOrDefault(j => j.Id == value.OpenerId) ?? Jingles[0];
        Closer = Jingles.FirstOrDefault(j => j.Id == value.CloserId) ?? Jingles[0];
        Source = Math.Max(0, Array.IndexOf(SourceKeys, value.Source));
        Folder = value.Folder;
        MaxAge = value.MaxAgeMinutes;
        Category = Categories.FirstOrDefault(c => c.Id == value.CategoryId);
        Feeds = value.Feeds;
        MaxHeadlines = value.MaxHeadlines;
        var a = value.Ai ?? new NewsAiOptionsDto();
        RegionalFeeds = a.RegionalFeeds;
        RegionalCount = a.RegionalCount;
        RegionalFirst = a.RegionalFirst;
        Words = a.Words;
        Attribution = a.Attribution;
        MaxAgeHours = a.MaxAgeHours;
        Bed = Jingles.FirstOrDefault(j => j.Id == a.BedId) ?? Jingles[0];
        Voice = Voices.FirstOrDefault(v => v.Id == a.VoiceId) ?? Voices[0];
        Place = a.Place;
        Latitude = a.Latitude;
        Longitude = a.Longitude;
        foreach (var d in Days) d.On = (value.Days & (1 << d.Index)) != 0;
        ClearCheck();
    }

    protected override void OnFirstShow() => _ = LoadAllAsync();

    private async Task LoadAllAsync()
    {
        if (await Hub.Client.GetAsync<List<MediaDto>>("/api/library?kind=jingle&limit=1000") is { } jingles)
            foreach (var m in jingles.Where(m => m.Enabled).OrderBy(m => m.DisplayName)) Jingles.Add(new NewsJingle(m.Id, m.DisplayName));
        if (await Hub.Client.GetAsync<List<CategoryDto>>("/api/categories") is { } cats)
            foreach (var c in cats) Categories.Add(c);
        if (await Hub.Client.GetAsync<List<VoiceProfileDto>>("/api/voice/profiles") is { } profiles)
            foreach (var p in profiles) Voices.Add(new NewsJingle(p.Id, string.IsNullOrWhiteSpace(p.Name) ? $"stem {p.Id}" : p.Name!));
        Opener ??= Jingles[0];
        Closer ??= Jingles[0];
        Bed ??= Jingles[0];
        Voice ??= Voices[0];
        await LoadBlocksAsync();
    }

    private async Task LoadBlocksAsync(long? select = null)
    {
        if (await Hub.Client.GetAsync<List<NewsBlockDto>>("/api/news") is not { } list) return;
        long? keep = select ?? Selected?.Id;
        Blocks.Clear();
        foreach (var b in list) Blocks.Add(b);
        Selected = Blocks.FirstOrDefault(b => b.Id == keep) ?? Blocks.FirstOrDefault();
        if (Selected is null) New();
    }

    private NewsBlockDto Current()
    {
        int days = Days.Where(d => d.On).Sum(d => 1 << d.Index);
        return new NewsBlockDto(Id, Name, Enabled, days, Hours, (int)(Minute ?? 0), (int)(Second ?? 0), Timing == 1 ? "soft" : "hard",
            Opener?.Id, Closer?.Id, SourceKeys[Math.Clamp(Source, 0, SourceKeys.Length - 1)], Folder, (int)(MaxAge ?? 60),
            Category?.Id, Feeds, (int)(MaxHeadlines ?? 5),
            Ai: new NewsAiOptionsDto(RegionalFeeds, (int)(RegionalCount ?? 3), RegionalFirst, (int)(Words ?? 120), Attribution,
                (int)(MaxAgeHours ?? 6), Bed?.Id, Voice?.Id, Place, Latitude, Longitude));
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        Id = 0;
        Name = "Nieuws";
        Enabled = true;
        Hours = "*";
        Minute = 0;
        Second = 0;
        Timing = 0;
        Opener = Jingles[0];
        Closer = Jingles[0];
        Source = 0;
        Folder = "";
        MaxAge = 60;
        Category = Categories.FirstOrDefault(c => c.Name.Contains("nieuws", StringComparison.OrdinalIgnoreCase));
        Feeds = "";
        MaxHeadlines = 5;
        RegionalFeeds = "";
        RegionalCount = 3;
        RegionalFirst = false;
        Words = 120;
        Attribution = true;
        MaxAgeHours = 6;
        Bed = Jingles[0];
        Voice = Voices[0];
        Place = "";
        Latitude = 0;
        Longitude = 0;
        foreach (var d in Days) d.On = true;
        ClearCheck();
    }

    // Examples to start from: short regional news on the half hour, national news on the hour, and the weather.
    public const string NosFeed = "https://feeds.nos.nl/nosnieuwsalgemeen | NOS";
    public const string RegionalExample = "https://www.rtvdrenthe.nl/rss/index.xml | RTV Drenthe";

    private NewsJingle JingleLike(string word) =>
        Jingles.FirstOrDefault(j => j.Name.Contains(word, StringComparison.OrdinalIgnoreCase)) ?? Jingles[0];

    [RelayCommand]
    private void Example(string? kind)
    {
        New();
        switch (kind)
        {
            case "kort":
                Name = "Kort regionaal nieuws (half uur)";
                Minute = 30;
                Timing = 1;
                Source = 3;
                Feeds = NosFeed;
                MaxHeadlines = 1;
                RegionalFeeds = RegionalExample;
                RegionalCount = 3;
                RegionalFirst = true;
                Words = 90;
                Opener = JingleLike("nieuws");
                break;
            case "weer":
                Name = "Weerbericht";
                Minute = 58;
                Timing = 1;
                Source = 4;
                Words = 70;
                Closer = JingleLike("weer");
                break;
            default:
                Name = "Nieuws op het hele uur";
                Source = 3;
                Feeds = NosFeed;
                MaxHeadlines = 4;
                RegionalFeeds = RegionalExample;
                RegionalCount = 2;
                Words = 200;
                Opener = JingleLike("nieuws");
                break;
        }
        CheckText = "Voorbeeld ingevuld. Pas aan wat je wilt en kies Opslaan.";
    }

    [RelayCommand]
    private async Task FindPlace()
    {
        Places.Clear();
        CheckProblem = null;
        var (list, error) = await Hub.Client.SendWithErrorAsync<List<PlaceDto>>(HttpMethod.Get,
            $"/api/news/places?name={Uri.EscapeDataString(PlaceQuery ?? "")}", null);
        if (list is null) { CheckProblem = error ?? "Plaats zoeken mislukt."; return; }
        foreach (var p in list) Places.Add(p);
        OnPropertyChanged(nameof(HasPlaces));
        PlaceChoice = Places.FirstOrDefault();
        if (Places.Count == 0) CheckProblem = $"Geen plaats gevonden voor “{PlaceQuery}”.";
    }

    /// <summary>Writes the bulletin with the language model as it would go on air; "stem" also speaks it into PFL.</summary>
    [RelayCommand]
    private async Task TryBlock(string? mode)
    {
        if (Busy) return;
        Busy = true;
        bool speak = mode == "stem";
        CheckText = speak ? "schrijven en inspreken… (kan een minuut duren)" : "schrijven met het taalmodel…";
        CheckProblem = null;
        Script = null;
        Note = null;
        try
        {
            var (r, error) = await Hub.Client.SendWithErrorAsync<NewsCheckDto>(HttpMethod.Post, "/api/news/try",
                new NewsTryRequest(Current(), Speak: speak, Play: speak));   // with the voice: load and play it on PFL
            if (r is null) { CheckText = ""; CheckProblem = error ?? "Proef mislukt: engine niet bereikbaar?"; return; }
            Show(r);
            if (r.Seconds is double s) CheckText += $"\n• ingesproken: {s:0} seconden, speelt op PFL (niet op de lucht)";
        }
        finally { Busy = false; }
    }

    private void Show(NewsCheckDto r)
    {
        CheckText = (r.Ok ? "Nu zou spelen:\n" : "Zou nu overgeslagen worden.\n") + string.Join("\n", r.Parts.Select(p => "• " + p));
        CheckProblem = r.Problem;
        Script = r.Script;
        Note = r.Note;
    }

    [RelayCommand]
    private async Task Save()
    {
        var saved = await Hub.Client.SendForAsync<NewsBlockDto>(HttpMethod.Post, "/api/news", Current());
        if (saved is null) { CheckProblem = "Opslaan mislukt."; return; }
        await LoadBlocksAsync(saved.Id);
        Hub.Status = $"nieuwsblok “{saved.Name}” opgeslagen";
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Id <= 0) { New(); return; }
        await Hub.Client.DeleteAsync($"/api/news/{Id}");
        Selected = null;
        await LoadBlocksAsync();
    }

    [RelayCommand]
    private async Task Check()
    {
        CheckText = "bezig…";
        CheckProblem = null;
        Script = null;
        var r = await Hub.Client.SendForAsync<NewsCheckDto>(HttpMethod.Post, "/api/news/check", Current());
        if (r is null) { CheckText = ""; CheckProblem = "Test mislukt: engine niet bereikbaar?"; return; }
        Show(r);
    }
}
