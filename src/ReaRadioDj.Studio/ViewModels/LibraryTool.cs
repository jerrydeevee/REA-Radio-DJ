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

    // ---- detail pane, selection, sorting, touch
    public List<SortChoice> Sorts { get; } =
    [
        new("Beste treffer", "relevance"), new("Artiest", "artist"), new("Titel", "title"), new("Jaar", "year"),
        new("BPM", "bpm"), new("Duur", "duration"), new("Laatst gespeeld", "lastplayed"), new("Vaakst gespeeld", "plays"),
        new("Nieuwste eerst", "added"),
    ];
    public List<ClickChoice> ClickActions { get; } =
    [
        new("Dubbelklik: voorluisteren", "pfl"), new("Dubbelklik: als volgende", "next"), new("Dubbelklik: achteraan", "end"),
        new("Dubbelklik: standby 1", "standby1"), new("Dubbelklik: cues", "cues"),
    ];
    [ObservableProperty] private SortChoice? _sort;
    [ObservableProperty] private ClickChoice? _doubleClick;
    [ObservableProperty] private bool _showDetails = true;
    [ObservableProperty] private bool _largeRows;
    /// <summary>Set by the view: the panel is wide enough for the detail pane next to the list.</summary>
    [ObservableProperty] private bool _wide = true;
    public bool DetailVisible => ShowDetails && Wide;
    partial void OnWideChanged(bool value) => OnPropertyChanged(nameof(DetailVisible));
    [ObservableProperty] private MediaDetailVm? _detail;
    [ObservableProperty] private int _selectionCount;
    [ObservableProperty] private CategoryChoice? _bulkCategory;
    [ObservableProperty] private string _bulkText = "";
    private List<MediaDto> _selection = [];
    public bool HasMulti => SelectionCount > 1;
    public string SelectionText => $"{SelectionCount} geselecteerd";

    partial void OnSelectionCountChanged(int value) { OnPropertyChanged(nameof(HasMulti)); OnPropertyChanged(nameof(SelectionText)); }
    partial void OnSortChanged(SortChoice? value) { if (value is null) return; Hub.Settings.LibrarySort = value.Key; Hub.Settings.Save(); Refresh(); }
    partial void OnDoubleClickChanged(ClickChoice? value) { if (value is null) return; Hub.Settings.LibraryDoubleClick = value.Key; Hub.Settings.Save(); }
    partial void OnShowDetailsChanged(bool value)
    {
        Hub.Settings.LibraryDetails = value; Hub.Settings.Save();
        OnPropertyChanged(nameof(DetailVisible));
        Detail = value && Selected is { } m ? new MediaDetailVm(this, m) : null;
    }
    partial void OnLargeRowsChanged(bool value) { Hub.Settings.LibraryLarge = value; Hub.Settings.Save(); }

    /// <summary>The view reports the selected rows (Ctrl and Shift select several).</summary>
    public void SetSelection(IEnumerable<MediaDto> items)
    {
        _selection = items.ToList();
        SelectionCount = _selection.Count;
        BulkText = "";
    }

    private IReadOnlyList<long> SelectedIds => _selection.Count > 0 ? _selection.Select(m => m.Id).ToList() : Selected is { } s ? [s.Id] : [];

    private async Task Bulk(MediaBulkRequest r, string done)
    {
        var res = await Hub.Client.SendForAsync<BulkResult>(HttpMethod.Put, "/api/media/bulk", r);
        BulkText = res is null ? "Niet gelukt." : string.Format(done, res.Changed) + " Ongedaan maken kan met Ctrl+Z.";
        Refresh();
        await LoadUndoAsync();
    }

    private sealed record BulkResult(int Changed);

    [RelayCommand] private Task BulkMove() => BulkCategory?.Id is long c ? Bulk(new MediaBulkRequest(SelectedIds, CategoryId: c), "{0} nummers verplaatst.") : Task.CompletedTask;
    [RelayCommand] private Task BulkOn() => Bulk(new MediaBulkRequest(SelectedIds, Enabled: true), "{0} nummers aangezet.");
    [RelayCommand] private Task BulkOff() => Bulk(new MediaBulkRequest(SelectedIds, Enabled: false), "{0} nummers uitgezet (worden niet meer ingepland).");
    [RelayCommand] private Task BulkClearDates() => Bulk(new MediaBulkRequest(SelectedIds, ClearDates: true), "Geldigheid van {0} nummers gewist.");

    [RelayCommand]
    private async Task BulkToLog()
    {
        int n = 0;
        foreach (var id in SelectedIds) if (await Hub.AddToLog(id, false)) n++;
        BulkText = $"{n} nummers achteraan in de log gezet.";
    }

    /// <summary>What a double click (or a tap on the touchscreen) does, as chosen by the operator.</summary>
    public void DoubleClickSelected()
    {
        switch (DoubleClick?.Key ?? "pfl")
        {
            case "next": NextCommand.Execute(null); break;
            case "end": EndCommand.Execute(null); break;
            case "standby1": StandbyCommand.Execute("standby1"); break;
            case "cues": CuesCommand.Execute(null); break;
            default: PflCommand.Execute(null); break;
        }
    }

    [RelayCommand] private Task PflStop() => Hub.PlayerCommand("pfl", "stop");

    private IEnumerable<MediaDto> Sorted(IEnumerable<MediaDto> items) => (Sort?.Key ?? "relevance") switch
    {
        "artist" => items.OrderBy(m => m.Artist, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase),
        "title" => items.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase),
        "year" => items.OrderByDescending(m => m.Year ?? 0),
        "bpm" => items.OrderBy(m => m.Bpm ?? double.MaxValue),
        "duration" => items.OrderBy(m => m.Duration),
        "lastplayed" => items.OrderByDescending(m => m.LastPlayed ?? DateTime.MinValue),
        "plays" => items.OrderByDescending(m => m.PlayCount),
        "added" => items.OrderByDescending(m => m.Added ?? DateTime.MinValue),
        _ => items,
    };
    // ---- saved searches ("slimme lijsten") and undo
    public ObservableCollection<SmartListDto> SmartLists { get; } = [];
    [ObservableProperty] private SmartListDto? _smartList;
    [ObservableProperty] private string _newListName = "";
    [ObservableProperty] private bool _showSaveList;
    [ObservableProperty] private string _undoText = "";
    [ObservableProperty] private bool _canUndo;
    private bool _applyingList;

    public async Task LoadListsAsync()
    {
        var lists = await Hub.Client.GetAsync<List<SmartListDto>>("/api/library/lists");
        if (lists is null) return;
        var keep = SmartList?.Id;
        _applyingList = true;
        SmartLists.Clear();
        foreach (var l in lists) SmartLists.Add(l);
        SmartList = SmartLists.FirstOrDefault(l => l.Id == keep);
        _applyingList = false;
    }

    /// <summary>Choosing a saved list puts its search, category and sort order back; the result is the state of now.</summary>
    partial void OnSmartListChanged(SmartListDto? value)
    {
        if (_applyingList || value is null) return;
        _applyingList = true;
        Category = CategoryChoices.FirstOrDefault(c => c.Id == value.CategoryId) ?? CategoryChoices.FirstOrDefault();
        if (value.Sort.Length > 0 && Sorts.FirstOrDefault(s => s.Key == value.Sort) is { } s) Sort = s;
        Query = value.Query;
        _applyingList = false;
        Refresh();
    }

    [RelayCommand] private void ToggleSaveList() { ShowSaveList = !ShowSaveList; if (ShowSaveList && NewListName.Length == 0) NewListName = SmartList?.Name ?? ""; }

    [RelayCommand]
    private async Task SaveList()
    {
        if (string.IsNullOrWhiteSpace(NewListName)) { BulkText = "Geef de lijst eerst een naam."; return; }
        var saved = await Hub.Client.SendForAsync<SmartListDto>(HttpMethod.Post, "/api/library/lists",
            new SmartListRequest(NewListName.Trim(), Query, Category?.Id, Sort?.Key ?? ""));
        if (saved is null) { BulkText = "Bewaren lukte niet."; return; }
        ShowSaveList = false; NewListName = "";
        await LoadListsAsync();
        _applyingList = true; SmartList = SmartLists.FirstOrDefault(l => l.Id == saved.Id); _applyingList = false;
    }

    [RelayCommand]
    private async Task DeleteList()
    {
        if (SmartList is not { } l) return;
        await Hub.Client.DeleteAsync($"/api/library/lists/{l.Id}");
        SmartList = null;
        await LoadListsAsync();
    }

    public async Task LoadUndoAsync()
    {
        var u = await Hub.Client.GetAsync<UndoDto>("/api/media/undo");
        CanUndo = u is { Label.Length: > 0 };
        UndoText = CanUndo ? $"↶ Ongedaan maken: {u!.Label}" : "";
    }

    [RelayCommand]
    private async Task Undo()
    {
        var u = await Hub.Client.SendForAsync<UndoDto>(HttpMethod.Post, "/api/media/undo", null);
        BulkText = u is not { Label.Length: > 0 } ? "Niets meer om ongedaan te maken." : $"Teruggezet: {u.Label}.";
        if (Detail is { } d) Detail = null; // shows the restored values once the list is fresh
        Refresh();
        await LoadUndoAsync();
    }

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

    // ---- folders and shares
    public ObservableCollection<LibraryFolderVm> Folders { get; } = [];
    [ObservableProperty] private string _newFolderPath = "";
    [ObservableProperty] private bool _newFolderIsJingle;
    [ObservableProperty] private string _newFolderCategory = "";
    [ObservableProperty] private bool _newFolderWriteTags;
    [ObservableProperty] private string _folderStatus = "";

    public async Task LoadFoldersAsync()
    {
        var list = await Hub.Client.GetAsync<List<LibraryFolderDto>>("/api/library/folders");
        if (list is null) return;
        Folders.Clear();
        foreach (var f in list) Folders.Add(new LibraryFolderVm(this, f));
    }

    [RelayCommand]
    private async Task AddFolder()
    {
        if (string.IsNullOrWhiteSpace(NewFolderPath)) { FolderStatus = "Kies eerst een map, of typ een pad zoals \\\\server\\muziek."; return; }
        FolderStatus = "Toevoegen…";
        var (f, error) = await Hub.Client.SendWithErrorAsync<LibraryFolderDto>(HttpMethod.Post, "/api/library/folders",
            new LibraryFolderRequest(NewFolderPath.Trim(), NewFolderIsJingle ? "jingle" : "song", NewFolderCategory.Trim(), true, NewFolderWriteTags));
        if (f is null) { FolderStatus = error ?? "Toevoegen mislukt."; return; }
        FolderStatus = $"{f.Path} toegevoegd. De bestanden worden nu ingelezen en gemeten; bekende nummers worden herkend.";
        NewFolderPath = ""; NewFolderCategory = "";
        await LoadFoldersAsync();
    }

    partial void OnQueryChanged(string value)
    {
        if (!_applyingList && SmartList is { } l && l.Query != value) { _applyingList = true; SmartList = null; _applyingList = false; }
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
    partial void OnCategoryChanged(CategoryChoice? value) { if (!_applyingList) Refresh(); }
    partial void OnSelectedChanged(MediaDto? value)
    {
        if (value is not null) Hub.SelectedMedia = value;
        if (ShowDetails && value is not null && Detail?.Id != value.Id) Detail = new MediaDetailVm(this, value);
        else if (value is null) Detail = null;
    }

    protected override void OnFirstShow()
    {
        Chips.Add(new FilterChip(this, "Mag nu", "is:mag", "Alleen wat nu ingepland mag worden (rotatie, geldigheid, aanwezig)"));
        Chips.Add(new FilterChip(this, "Nieuw", "is:nieuw", "De laatste 30 dagen toegevoegd"));
        Chips.Add(new FilterChip(this, "Nooit gespeeld", "is:nooit", "Nog nooit uitgezonden"));
        Chips.Add(new FilterChip(this, "Lang niet gespeeld", "is:lang", "Meer dan 30 dagen niet uitgezonden"));
        Chips.Add(new FilterChip(this, "Korter dan 3:30", "duur:<3:30", "Handig vlak voor het nieuws of een vaste tijd"));
        Chips.Add(new FilterChip(this, "Zonder cues", "is:zondercues", "Cues nog niet met de hand nagekeken"));
        Chips.Add(new FilterChip(this, "Mag nu niet", "is:geblokkeerd", "Mag nu niet: rotatie, verlopen, uitgezet of bestand weg"));
        Chips.Add(new FilterChip(this, "Aandacht nodig", "is:aandacht", "Kwaliteitscontrole: te zacht of te hard, clipping, stilte, lage bitrate of dubbel"));
        Chips.Add(new FilterChip(this, "Ontbreekt", "is:ontbreekt", "Het bestand staat niet meer in de map"));
        Decade = Decades[0];
        _showDetails = Hub.Settings.LibraryDetails;
        _largeRows = Hub.Settings.LibraryLarge;
        OnPropertyChanged(nameof(ShowDetails)); OnPropertyChanged(nameof(LargeRows)); OnPropertyChanged(nameof(DetailVisible));
        _sort = Sorts.FirstOrDefault(s => s.Key == Hub.Settings.LibrarySort) ?? Sorts[0];
        _doubleClick = ClickActions.FirstOrDefault(c => c.Key == Hub.Settings.LibraryDoubleClick) ?? ClickActions[0];
        OnPropertyChanged(nameof(Sort)); OnPropertyChanged(nameof(DoubleClick));
        Hub.LibraryChanged += () => { FillCategories(); Refresh(); _ = LoadFoldersAsync(); _ = LoadListsAsync(); _ = LoadUndoAsync(); };
        FillCategories();
        Refresh();
        _ = LoadFoldersAsync();
        _ = LoadListsAsync();
        _ = LoadUndoAsync();
    }

    protected override void OnState(StateDto s)
    {
        Status = $"{s.Library.Songs} nummers · {s.Library.Jingles} jingles · {s.Library.Status}";
        // The engine watches the folders; when a scan finished or another song started, the list (and its "may play now") is fresh again.
        string key = $"{s.Library.Songs}/{s.Library.Jingles}/{s.Library.Status}";
        bool scanning = s.Library.Status.StartsWith("Analyseren", StringComparison.Ordinal) || s.Library.Status.StartsWith("Inlezen", StringComparison.Ordinal)
            || s.Library.Status.StartsWith("Beat", StringComparison.Ordinal);
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
                foreach (var i in Sorted(items)) Items.Add(i);
                Selected = Items.FirstOrDefault(i => i.Id == keep);
                if (Detail is { } d && Items.FirstOrDefault(i => i.Id == d.Id) is { } fresh && fresh != d.Media && ShowDetails) Detail = new MediaDetailVm(this, fresh);
                int blocked = items.Count(i => !i.Allowed);
                ResultText = items.Count == 0
                    ? (Query.Length > 0 ? "Niets gevonden. Typ ? voor de zoekmogelijkheden." : "De bibliotheek is leeg.")
                    : $"{items.Count}{(items.Count >= 1000 ? "+" : "")} gevonden" + (blocked > 0 ? $" · {blocked} mogen nu niet" : "");
            });
        }, cts.Token);
    }

    private long? SelId => Selected?.Id;

    [RelayCommand] private Task Next() => SelId is long id ? Hub.AddToLog(id, true) : Task.CompletedTask;
    [RelayCommand] private Task End() => _selection.Count > 1 ? BulkToLog() : SelId is long id ? Hub.AddToLog(id, false) : Task.CompletedTask;
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
        await LoadUndoAsync();
    }

    [RelayCommand]
    private async Task ToggleEnabled()
    {
        if (Selected is not { } m) return;
        await Hub.Client.PutAsync($"/api/media/{m.Id}", new MediaUpdateRequest(m.CategoryId, !m.Enabled, m.ValidFrom, m.ValidUntil));
        Refresh();
        await LoadUndoAsync();
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
