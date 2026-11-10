using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>One folder of the library in the folder list: its settings, what the last scan found, and removing it.</summary>
public sealed partial class LibraryFolderVm : ObservableObject
{
    private readonly LibraryTool _owner;
    private bool _loading;

    public LibraryFolderVm(LibraryTool owner, LibraryFolderDto f)
    {
        _owner = owner;
        Id = f.Id;
        Path = f.Path;
        Fixed = f.Fixed;
        KindText = f.Kind == "jingle" ? "jingles" : "muziek";
        _loading = true;
        Category = f.Category;
        Watch = f.Watch;
        WriteTags = f.WriteTags;
        Enabled = f.Enabled;
        _loading = false;
        StateText = f.Reachable switch
        {
            null => f.Enabled ? "nog niet ingelezen" : "uitgezet",
            false => f.Error,
            true => $"{f.Files} bestanden · {f.Scanned:HH:mm}",
        };
        Offline = f.Reachable == false;
    }

    public long Id { get; }
    public string Path { get; }
    public bool Fixed { get; }
    public bool Editable => !Fixed;
    public string KindText { get; }
    public string StateText { get; }
    public bool Offline { get; }
    public string Title => Fixed ? $"{Path}  (vaste {KindText}map uit de instellingen)" : Path;

    [ObservableProperty] private string _category = "";
    [ObservableProperty] private bool _watch;
    [ObservableProperty] private bool _writeTags;
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _removeText = "Verwijderen";
    private bool _confirm;

    partial void OnWatchChanged(bool value) => Save();
    partial void OnWriteTagsChanged(bool value) => Save();
    partial void OnEnabledChanged(bool value) => Save();

    [RelayCommand] private void SaveCategory() => Save();

    private void Save()
    {
        if (_loading || Fixed) return;
        _ = Send();
        async Task Send()
        {
            await StudioHub.Current.Client.SendForAsync<LibraryFolderDto>(HttpMethod.Put, $"/api/library/folders/{Id}",
                new LibraryFolderRequest(Path, KindText == "jingles" ? "jingle" : "song", Category, Watch, WriteTags, Enabled));
            await _owner.LoadFoldersAsync();
        }
    }

    [RelayCommand]
    private async Task Remove()
    {
        if (Fixed) return;
        // Two clicks: the first asks, the second removes. The tracks and their data are kept.
        if (!_confirm) { _confirm = true; RemoveText = "Zeker? Klik nogmaals"; return; }
        var r = await StudioHub.Current.Client.SendForAsync<RemovedDto>(HttpMethod.Delete, $"/api/library/folders/{Id}", null);
        _owner.FolderStatus = r is null ? "Verwijderen mislukt." : $"Map verwijderd. {r.Kept} nummers zijn met al hun gegevens bewaard en komen terug als ze weer gevonden worden.";
        await _owner.LoadFoldersAsync();
        StudioHub.Current.NotifyLibraryChanged();
    }

    private sealed record RemovedDto(int Kept);
}
