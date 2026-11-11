using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One folder or audio file in the Verkenner.</summary>
public sealed class ExplorerEntry(FileEntryDto e)
{
    public FileEntryDto Dto { get; } = e;
    public string Name => Dto.Name;
    public string Path => Dto.Path;
    public bool IsFolder => Dto.IsFolder;
    public bool InLibrary => Dto.MediaId is not null;
    public string Icon => Dto.IsFolder ? "📁" : "♪";
    public string Info => Dto.IsFolder ? "" :
        (Dto.Size >= 1 << 20 ? $"{(Dto.Size / 1048576.0).ToString("0.0", Nl)} MB" : $"{Math.Max(1, Dto.Size / 1024)} kB")
        + (InLibrary ? " · bibliotheek" : "");
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");
}

/// <summary>A shortcut on the left of the Verkenner: a library folder, a saved favourite or the list of drives.</summary>
public sealed record ExplorerPlace(string Name, string Path, bool Favourite);

/// <summary>
/// Verkenner: browse the folders of the engine machine and put an audio file on the log, in PFL or (when it is in the
/// library) in the other windows, also by dragging. Files outside the library are analysed by the engine when used.
/// </summary>
public sealed partial class ExplorerTool : StudioTool
{
    public ExplorerTool() : base("explorer", "Verkenner") { }

    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string? _parent;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private ExplorerEntry? _selected;
    [ObservableProperty] private bool _busy;
    private List<ExplorerEntry> _all = [];

    public ObservableCollection<ExplorerEntry> Entries { get; } = [];
    public ObservableCollection<ExplorerPlace> Places { get; } = [];
    public bool HasFile => Selected is { IsFolder: false };
    public bool CanGoUp => Parent is not null;
    public bool IsFavourite => Places.Any(p => p.Favourite && SamePath(p.Path, Path));
    public string Heading => Path.Length == 0 ? "Deze computer" : Path;

    protected override void OnFirstShow()
    {
        _ = LoadPlacesAsync();
        _ = OpenAsync(Hub.Settings.ExplorerPath);
    }

    private async Task LoadPlacesAsync()
    {
        Places.Clear();
        Places.Add(new ExplorerPlace("Deze computer", "", false));
        foreach (var f in Hub.Settings.ExplorerFavorites) Places.Add(new ExplorerPlace("★ " + Last(f), f, true));
        var folders = await Hub.Client.GetAsync<List<LibraryFolderDto>>("/api/library/folders") ?? [];
        foreach (var f in folders.Where(f => f.Enabled))
            if (!Places.Any(p => SamePath(p.Path, f.Path))) Places.Add(new ExplorerPlace(Last(f.Path), f.Path, false));
        OnPropertyChanged(nameof(IsFavourite));
    }

    public async Task OpenAsync(string? path)
    {
        Busy = true;
        var l = await Hub.Client.GetAsync<FolderDto>("/api/files?path=" + Uri.EscapeDataString(path ?? ""));
        Busy = false;
        if (l is null) { Status = "De engine geeft geen antwoord."; return; }
        if (l.Error is not null && l.Entries.Count == 0 && !string.IsNullOrEmpty(path) && path != Path)
        {
            // A saved folder that is gone: fall back to the drives instead of an empty window.
            if (string.IsNullOrEmpty(Path)) { await OpenAsync(""); Status = l.Error; }
            else Status = l.Error;
            return;
        }
        Path = l.Path;
        Parent = l.Parent;
        _all = [.. l.Entries.Select(e => new ExplorerEntry(e))];
        Filter = "";
        ApplyFilter();
        int files = _all.Count(e => !e.IsFolder);
        Status = l.Error ?? (l.Path.Length == 0 ? $"{_all.Count} schijven" : $"{_all.Count - files} mappen, {files} audiobestanden");
        if (Hub.Settings.ExplorerPath != l.Path) { Hub.Settings.ExplorerPath = l.Path; Hub.Settings.Save(); }
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(IsFavourite));
        OnPropertyChanged(nameof(Heading));
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = Filter.Trim();
        Entries.Clear();
        foreach (var e in _all.Where(e => f.Length == 0 || e.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase))) Entries.Add(e);
    }

    partial void OnSelectedChanged(ExplorerEntry? value)
    {
        OnPropertyChanged(nameof(HasFile));
        // A library file also becomes the library selection, so Itemgegevens, Cues and the cartwall follow it.
        if (value?.Dto.MediaId is long id) _ = SelectMediaAsync(id);
    }

    private async Task SelectMediaAsync(long id)
    {
        if (await Hub.Client.GetAsync<MediaDto>($"/api/media/{id}") is { } m && Selected?.Dto.MediaId == id) Hub.SelectedMedia = m;
    }

    /// <summary>Double click or Enter: open a folder, or put a file at the end of the log.</summary>
    public Task Activate(ExplorerEntry e) => e.IsFolder ? OpenAsync(e.Path) : ToLogAsync(e, null);

    [RelayCommand] private Task Up() => OpenAsync(Parent ?? "");
    [RelayCommand] private Task Refresh() => OpenAsync(Path);
    [RelayCommand] private Task Go(ExplorerPlace? p) => p is null ? Task.CompletedTask : OpenAsync(p.Path);
    [RelayCommand] private Task Next() => Selected is { IsFolder: false } e ? ToLogAsync(e, 0) : Task.CompletedTask;
    [RelayCommand] private Task End() => Selected is { IsFolder: false } e ? ToLogAsync(e, null) : Task.CompletedTask;

    [RelayCommand]
    private async Task Pfl()
    {
        if (Selected is not { IsFolder: false } e) return;
        Status = $"{e.Name} in PFL laden…";
        var (_, err) = await Hub.Client.SendWithErrorAsync<object>(HttpMethod.Post, "/api/files/pfl", new FileRequest(e.Path, null));
        Status = err is null ? $"{e.Name} speelt in PFL." : $"PFL lukt niet: {err}";
    }

    [RelayCommand]
    private void Favourite()
    {
        if (Path.Length == 0) return;
        var list = Hub.Settings.ExplorerFavorites;
        if (list.RemoveAll(f => SamePath(f, Path)) == 0) list.Add(Path);
        Hub.Settings.Save();
        _ = LoadPlacesAsync();
    }

    private async Task ToLogAsync(ExplorerEntry e, int? position)
    {
        Status = e.InLibrary ? $"{e.Name} op de log zetten…" : $"{e.Name} analyseren en op de log zetten…";
        var (_, err) = e.Dto.MediaId is long id
            ? await Hub.Client.SendWithErrorAsync<object>(HttpMethod.Post, "/api/log/media", new AddToLogRequest(id, position))
            : await Hub.Client.SendWithErrorAsync<object>(HttpMethod.Post, "/api/files/log", new FileRequest(e.Path, position));
        Status = err is null ? $"{e.Name} staat {(position == 0 ? "als volgende" : "achteraan")} in de log." : $"Niet gelukt: {err}";
    }

    private static string Last(string path)
    {
        var t = path.TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(t);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
