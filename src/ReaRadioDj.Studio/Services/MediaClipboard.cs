using System.Collections.ObjectModel;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// The clipboard for tracks: a fixed place where tracks are parked (from the library, the log, a deck or a player)
/// and dragged out again later, also to another work sheet. One list for the whole studio, kept in the studio
/// settings, so it survives a restart. Newest on top; a track that is already there moves to the top.
/// </summary>
public static class MediaClipboard
{
    public const int Max = 50;

    public static ObservableCollection<MediaDto> Items { get; } = [];

    public static event Action? Changed;

    private static bool _loaded;

    private static StudioHub Hub => StudioHub.Current;

    /// <summary>Fills the list from the saved ids the first time (tracks no longer in the library drop out).</summary>
    public static async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (var id in Hub.Settings.Clipboard.Take(Max).ToList())
            if (await Hub.Client.GetAsync<MediaDto>($"/api/media/{id}") is { } m && Items.All(x => x.Id != m.Id))
                Items.Add(m);
        Changed?.Invoke();
    }

    public static async Task AddAsync(IEnumerable<long> ids)
    {
        await EnsureLoadedAsync();
        // in reverse, so the first of a selection ends on top
        foreach (var id in ids.Reverse().ToList())
        {
            var media = Items.FirstOrDefault(x => x.Id == id) ?? await Hub.Client.GetAsync<MediaDto>($"/api/media/{id}");
            if (media is null) continue;
            if (Items.IndexOf(media) is var at and >= 0) Items.RemoveAt(at);
            Items.Insert(0, media);
        }
        while (Items.Count > Max) Items.RemoveAt(Items.Count - 1);
        Save();
    }

    public static Task AddAsync(long id) => AddAsync([id]);

    public static void Remove(MediaDto media)
    {
        if (Items.Remove(media)) Save();
    }

    public static void Clear()
    {
        Items.Clear();
        Save();
    }

    private static void Save()
    {
        Hub.Settings.Clipboard = Items.Select(x => x.Id).ToList();
        Hub.Settings.Save();
        Changed?.Invoke();
    }
}
