using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;
using ReaRadioDj.Studio.Services;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// Wish list from Spotify playlists: the editors pick music in Spotify, the studio imports the metadata (title, artist,
/// ISRC, release), shows which tracks the library already has and lists the missing ones to acquire. Tracks in the library
/// go straight into the log. No audio comes from Spotify.
/// </summary>
public sealed partial class WishlistTool : StudioTool
{
    public static readonly string[] Filters = ["Alles", "Ontbreekt nog", "In de bibliotheek"];
    private List<WishItemDto> _all = [];

    public WishlistTool() : base("wishlist", "Wensenlijst (Spotify)") { }

    public ObservableCollection<WishItemDto> Items { get; } = [];
    public ObservableCollection<string> Playlists { get; } = [];
    public IReadOnlyList<string> FilterChoices => Filters;
    [ObservableProperty] private string _link = "";
    [ObservableProperty] private int _filter;
    [ObservableProperty] private string? _playlist;
    [ObservableProperty] private WishItemDto? _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _configured = true;
    [ObservableProperty] private bool _busy;

    partial void OnFilterChanged(int value) => Fill();
    partial void OnPlaylistChanged(string? value) => Fill();

    protected override void OnFirstShow() => _ = ReloadAsync();

    [RelayCommand]
    private async Task ReloadAsync()
    {
        var status = await Hub.Client.GetAsync<WishStatusDto>("/api/wishlist/status");
        var list = await Hub.Client.GetAsync<List<WishItemDto>>("/api/wishlist");
        if (status is null || list is null) return;
        Configured = status.Configured;
        _all = list;
        var keep = Playlist;
        Playlists.Clear();
        Playlists.Add("(alle playlists)");
        foreach (var p in status.Playlists) Playlists.Add(p);
        Playlist = Playlists.Contains(keep ?? "") ? keep : Playlists[0];
        Status = status.Total == 0 ? "nog leeg: plak een link naar een openbare Spotify-playlist"
            : $"{status.Total} nummers · {status.InLibrary} in de bibliotheek · {status.Total - status.InLibrary} ontbreken";
        Fill();
    }

    private void Fill()
    {
        IEnumerable<WishItemDto> q = _all;
        if (Playlist is { } p && p != "(alle playlists)") q = q.Where(w => w.Playlist == p);
        q = Filter switch { 1 => q.Where(w => !w.InLibrary), 2 => q.Where(w => w.InLibrary), _ => q };
        Items.Clear();
        foreach (var w in q) Items.Add(w);
    }

    [RelayCommand]
    private async Task Import()
    {
        if (Busy || string.IsNullOrWhiteSpace(Link)) return;
        Busy = true;
        Status = "playlist ophalen bij Spotify…";
        try
        {
            var r = await Hub.Client.SendForAsync<WishImportResultDto>(HttpMethod.Post, "/api/wishlist/import", new WishImportRequest(Link));
            if (r is null) { Status = "geen antwoord van de engine"; return; }
            if (!r.Ok) { Status = r.Error ?? "import mislukt"; return; }
            Link = "";
            await ReloadAsync();
            Playlist = r.Playlist;
            Status = $"'{r.Playlist}': {r.Total} nummers ({r.Added} nieuw) · {r.InLibrary} al in de bibliotheek · {r.Total - r.InLibrary} ontbreken";
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task Match()
    {
        await Hub.Client.PostAsync("/api/wishlist/match");
        await ReloadAsync();
    }

    [RelayCommand] private Task AddEnd(WishItemDto? w) => w?.MediaId is long id ? Hub.AddToLog(id, false) : Task.CompletedTask;
    [RelayCommand] private Task AddNext(WishItemDto? w) => w?.MediaId is long id ? Hub.AddToLog(id, true) : Task.CompletedTask;

    /// <summary>All tracks of the shown list that the library has, at the end of the log (in playlist order).</summary>
    [RelayCommand]
    private async Task AddAllToLog()
    {
        int n = 0;
        foreach (var w in Items.Where(w => w.MediaId is not null).ToList())
            if (await Hub.AddToLog(w.MediaId!.Value, false)) n++;
        Hub.Status = $"{n} nummers achteraan in de log gezet";
    }

    [RelayCommand]
    private async Task Delete(WishItemDto? w)
    {
        if (w is not null && await Hub.Client.DeleteAsync($"/api/wishlist/{w.Id}")) await ReloadAsync();
    }

    [RelayCommand]
    private async Task DeletePlaylist()
    {
        if (Playlist is not { } p || p == "(alle playlists)") return;
        if (await Hub.Client.DeleteAsync($"/api/wishlist?playlist={Uri.EscapeDataString(p)}")) { Playlist = null; await ReloadAsync(); }
    }

    /// <summary>Opens the track on Spotify in the browser (to listen while deciding).</summary>
    [RelayCommand]
    private static void OpenLink(WishItemDto? w)
    {
        if (w is { Link.Length: > 0 } && w.Link.StartsWith("https://open.spotify.com/", StringComparison.Ordinal))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(w.Link) { UseShellExecute = true });
    }
}
