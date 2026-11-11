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
/// from a category, or RSS headlines read out by the AI voice) and a closer. Test shows what a block would play now.
/// </summary>
public sealed partial class NewsTool : StudioTool
{
    public NewsTool() : base("news", "Nieuws") { }

    public static readonly string[] SourceNames = ["Nieuwste bestand in een map", "Item uit een categorie", "Koppen uit RSS, voorgelezen door de AI-stem"];
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

    public bool IsFolder => Source == 0;
    public bool IsCategory => Source == 1;
    public bool IsHeadlines => Source == 2;
    public bool HasSelection => Selected is not null || Id == 0;

    partial void OnSourceChanged(int value)
    {
        OnPropertyChanged(nameof(IsFolder)); OnPropertyChanged(nameof(IsCategory)); OnPropertyChanged(nameof(IsHeadlines));
        ClearCheck();
    }

    private void ClearCheck() { CheckText = ""; CheckProblem = null; Script = null; }

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
        Source = value.Source switch { "category" => 1, "headlines" => 2, _ => 0 };
        Folder = value.Folder;
        MaxAge = value.MaxAgeMinutes;
        Category = Categories.FirstOrDefault(c => c.Id == value.CategoryId);
        Feeds = value.Feeds;
        MaxHeadlines = value.MaxHeadlines;
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
        Opener ??= Jingles[0];
        Closer ??= Jingles[0];
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
            Opener?.Id, Closer?.Id, Source switch { 1 => "category", 2 => "headlines", _ => "folder" }, Folder, (int)(MaxAge ?? 60),
            Category?.Id, Feeds, (int)(MaxHeadlines ?? 5));
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
        foreach (var d in Days) d.On = true;
        ClearCheck();
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
        CheckText = (r.Ok ? "Nu zou spelen:\n" : "Zou nu overgeslagen worden.\n") + string.Join("\n", r.Parts.Select(p => "• " + p));
        CheckProblem = r.Problem;
        Script = r.Script;
    }
}
