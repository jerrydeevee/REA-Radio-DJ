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

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private CategoryChoice? _category;
    [ObservableProperty] private MediaDto? _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _newCategory = "";
    [ObservableProperty] private bool _newCategoryIsJingle;

    partial void OnQueryChanged(string value) => Refresh();
    partial void OnCategoryChanged(CategoryChoice? value) => Refresh();
    partial void OnSelectedChanged(MediaDto? value) { if (value is not null) Hub.SelectedMedia = value; }

    protected override void OnFirstShow()
    {
        Hub.LibraryChanged += () => { FillCategories(); Refresh(); };
        FillCategories();
        Refresh();
    }

    protected override void OnState(StateDto s) => Status = $"{s.Library.Songs} nummers · {s.Library.Jingles} jingles · {s.Library.Status}";

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
