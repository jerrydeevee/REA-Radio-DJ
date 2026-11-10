using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

public sealed record CategoryChoice(long? Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A filter button above the library: switching it on adds its short filter to the search box, so people learn the syntax.</summary>
public sealed partial class FilterChip(LibraryTool owner, string label, string token, string tip) : ObservableObject
{
    public string Label { get; } = label;
    public string Token { get; } = token;
    public string Tip { get; } = tip;
    internal bool Syncing;
    [ObservableProperty] private bool _isOn;

    partial void OnIsOnChanged(bool value)
    {
        if (!Syncing) owner.SetToken(Token, value);
    }
}

public sealed record DecadeChoice(string Name, string Token)
{
    public override string ToString() => Name;
}

/// <summary>
/// The library: search, filter by category, and send items to the log, the players, the PFL or the cue editor.
/// Items can be dragged to the log, the players and the cartwalls.
/// </summary>
public sealed partial class LibraryTool : StudioTool
{
    private CancellationTokenSource? _search;

    public LibraryTool() : base("library", "Bibliotheek") { }

    public ObservableCollection<MediaDto> Items { get; } = [];
    public ObservableCollection<CategoryChoice> CategoryChoices { get; } = [];
    public ObservableCollection<FilterChip> Chips { get; } = [];
    public ObservableCollection<SearchHelpDto> HelpLines { get; } = [];
    public List<DecadeChoice> Decades { get; } =
    [
        new("Alle jaren", ""), new("jaren 60", "jaar:196"), new("jaren 70", "jaar:197"), new("jaren 80", "jaar:198"),
        new("jaren 90", "jaar:199"), new("jaren 00", "jaar:200"), new("jaren 10", "jaar:201"), new("jaren 20", "jaar:202"),
    ];
    [ObservableProperty] private DecadeChoice? _decade;
    [ObservableProperty] private bool _showHelp;
    [ObservableProperty] private string _resultText = "";
    private string _libraryKey = "";
    private long? _playingId;

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private CategoryChoice? _category;
    [ObservableProperty] private MediaDto? _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _newCategory = "";
    [ObservableProperty] private bool _newCategoryIsJingle;

    partial void OnQueryChanged(string value)
    {
        ShowHelp = value.Trim() == "?";
        SyncChips();
        if (ShowHelp) { _ = LoadHelpAsync(); return; }
        Refresh();
    }

    partial void OnDecadeChanged(DecadeChoice? value)
    {
        if (_syncing) return;
        var tokens = Split(Query).Where(t => !t.StartsWith("jaar:", StringComparison.OrdinalIgnoreCase)).ToList();
        if (value is { Token.Length: > 0 }) tokens.Add(value.Token);
        Query = string.Join(' ', tokens);
    }

    private bool _syncing;

    /// <summary>Adds or removes one short filter in the search box.</summary>
    internal void SetToken(string token, bool on)
    {
        var tokens = Split(Query).Where(t => !t.Equals(token, StringComparison.OrdinalIgnoreCase)).ToList();
        if (on) tokens.Add(token);
        Query = string.Join(' ', tokens);
    }

    private void SyncChips()
    {
        _syncing = true;
        var tokens = Split(Query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var c in Chips) { c.Syncing = true; c.IsOn = tokens.Contains(c.Token); c.Syncing = false; }
        Decade = Decades.FirstOrDefault(d => d.Token.Length > 0 && tokens.Contains(d.Token)) ?? Decades[0];
        _syncing = false;
    }

    private static IEnumerable<string> Split(string q) => q.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private async Task LoadHelpAsync()
    {
        if (HelpLines.Count > 0) return;
        var lines = await Hub.Client.GetAsync<List<SearchHelpDto>>("/api/library/help");
        foreach (var l in lines ?? []) HelpLines.Add(l);
    }

    [RelayCommand] private void ClearSearch() => Query = "";
    [RelayCommand] private void ToggleHelp() => Query = ShowHelp ? "" : "?";
    partial void OnCategoryChanged(CategoryChoice? value) => Refresh();
    partial void OnSelectedChanged(MediaDto? value) { if (value is not null) Hub.SelectedMedia = value; }

    protected override void OnFirstShow()
    {
        Chips.Add(new FilterChip(this, "Mag nu", "is:mag", "Alleen wat nu ingepland mag worden (rotatie, geldigheid, aanwezig)"));
        Chips.Add(new FilterChip(this, "Nieuw", "is:nieuw", "De laatste 30 dagen toegevoegd"));
        Chips.Add(new FilterChip(this, "Nooit gespeeld", "is:nooit", "Nog nooit uitgezonden"));
        Chips.Add(new FilterChip(this, "Lang niet gespeeld", "is:lang", "Meer dan 30 dagen niet uitgezonden"));
        Chips.Add(new FilterChip(this, "Korter dan 3:30", "duur:<3:30", "Handig vlak voor het nieuws of een vaste tijd"));
        Chips.Add(new FilterChip(this, "Zonder cues", "is:zondercues", "Cues nog niet met de hand nagekeken"));
        Chips.Add(new FilterChip(this, "Aandacht nodig", "is:geblokkeerd", "Mag nu niet: rotatie, verlopen, uitgezet of bestand weg"));
        Chips.Add(new FilterChip(this, "Ontbreekt", "is:ontbreekt", "Het bestand staat niet meer in de map"));
        Decade = Decades[0];
        Hub.LibraryChanged += () => { FillCategories(); Refresh(); };
        FillCategories();
        Refresh();
    }

    protected override void OnState(StateDto s)
    {
        Status = $"{s.Library.Songs} nummers · {s.Library.Jingles} jingles · {s.Library.Status}";
        // The engine watches the folders; when a scan finished or another song started, the list (and its "may play now") is fresh again.
        string key = $"{s.Library.Songs}/{s.Library.Jingles}/{s.Library.Status}";
        bool scanning = s.Library.Status.StartsWith("Analyseren", StringComparison.Ordinal) || s.Library.Status.StartsWith("Inlezen", StringComparison.Ordinal);
        long? playing = s.Engine.Current?.Id;
        if (!scanning && key != _libraryKey && _libraryKey.Length > 0) _ = Hub.RefreshCategoriesAsync();
        else if (playing != _playingId && _playingId is not null && !ShowHelp) Refresh();
        if (!scanning) _libraryKey = key;
        _playingId = playing;
    }

    private void FillCategories()
    {
        var keep = Category?.Id;
        CategoryChoices.Clear();
        CategoryChoices.Add(new CategoryChoice(null, "Alle categorieën"));
        foreach (var c in Hub.Categories) CategoryChoices.Add(new CategoryChoice(c.Id, $"{c.Name} ({(c.Kind == "jingle" ? "jingle" : "muziek")})"));
        Category = CategoryChoices.FirstOrDefault(c => c.Id == keep) ?? CategoryChoices[0];
    }

    public void Refresh()
    {
        _search?.Cancel();
        var cts = _search = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            await Task.Delay(200, cts.Token);
            var url = $"/api/library?limit=1000&q={Uri.EscapeDataString(Query)}" + (Category?.Id is long id ? $"&category={id}" : "");
            var items = await Hub.Client.GetAsync<List<MediaDto>>(url);
            if (items is null || cts.IsCancellationRequested) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var keep = Selected?.Id;
                Items.Clear();
                foreach (var i in items) Items.Add(i);
                Selected = Items.FirstOrDefault(i => i.Id == keep);
                int blocked = items.Count(i => !i.Allowed);
                ResultText = items.Count == 0
                    ? (Query.Length > 0 ? "Niets gevonden. Typ ? voor de zoekmogelijkheden." : "De bibliotheek is leeg.")
                    : $"{items.Count}{(items.Count >= 1000 ? "+" : "")} gevonden" + (blocked > 0 ? $" · {blocked} mogen nu niet" : "");
            });
        }, cts.Token);
    }

    private long? SelId => Selected?.Id;

    [RelayCommand] private Task Next() => SelId is long id ? Hub.AddToLog(id, true) : Task.CompletedTask;
    [RelayCommand] private Task End() => SelId is long id ? Hub.AddToLog(id, false) : Task.CompletedTask;
    [RelayCommand] private Task Standby(string player) => SelId is long id ? Hub.LoadPlayer(player, id) : Task.CompletedTask;

    [RelayCommand]
    private async Task Pfl()
    {
        if (SelId is not long id) return;
        if (await Hub.LoadPlayer("pfl", id)) await Hub.PlayerCommand("pfl", "play");
    }

    [RelayCommand] private Task Overlay() => SelId is long id ? Hub.Client.PostAsync("/api/overlays/play", new MediaRequest(id)) : Task.CompletedTask;
    [RelayCommand] private void Cues() { if (SelId is long id) Hub.RequestCueEditor(id); }

    [RelayCommand]
    private async Task Scan()
    {
        await Hub.Client.PostAsync("/api/library/scan");
        await Task.Delay(1500);
        await Hub.RefreshCategoriesAsync();
    }

    [RelayCommand]
    private async Task MoveToCategory(CategoryChoice? choice)
    {
        if (Selected is not { } m || choice is null) return;
        await Hub.Client.PutAsync($"/api/media/{m.Id}", new MediaUpdateRequest(choice.Id, m.Enabled, m.ValidFrom, m.ValidUntil));
        Refresh();
    }

    [RelayCommand]
    private async Task ToggleEnabled()
    {
        if (Selected is not { } m) return;
        await Hub.Client.PutAsync($"/api/media/{m.Id}", new MediaUpdateRequest(m.CategoryId, !m.Enabled, m.ValidFrom, m.ValidUntil));
        Refresh();
    }

    [RelayCommand]
    private async Task AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategory)) return;
        if (await Hub.Client.PostAsync("/api/categories", new CategoryRequest(NewCategory.Trim(), NewCategoryIsJingle ? "jingle" : "song", null)))
        {
            NewCategory = "";
            await Hub.RefreshCategoriesAsync();
        }
    }
}
