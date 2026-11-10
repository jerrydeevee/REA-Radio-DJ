using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>
/// The Verkenner: browse the folders of the engine machine (read-only) and put an audio file on the log or in PFL, also
/// when it is not in the library. Paths are always paths on the engine machine, so a remote studio sees the same files.
/// </summary>
public static class FilesApi
{
    /// <summary>Shorter files are put on the log as a jingle, longer ones as a song.</summary>
    public const double JingleSeconds = 60;

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/files");
        g.MapGet("", (string? path, MediaLibrary library) =>
        {
            var l = FileBrowser.List(path);
            var ids = l.Path.Length > 0 && l.Error is null ? library.IdsIn(l.Path) : new Dictionary<string, long>();
            return new FolderDto(l.Path, l.Parent,
                l.Entries.Select(e => new FileEntryDto(e.Name, e.Path, e.IsFolder, e.Size, e.Modified,
                    ids.TryGetValue(e.Path, out var id) ? id : null)).ToList(), l.Error);
        });

        g.MapPost("/log", async (FileRequest r, PlayoutLog log, MediaLibrary library) =>
        {
            if (await LoadAsync(r.Path, library) is not { } media) return Results.BadRequest("Geen afspeelbaar audiobestand.");
            var item = new LogItem { Type = media.Kind == MediaKind.Song ? LogItemType.Song : LogItemType.Jingle, Media = media, Origin = "verkenner" };
            log.Insert(Math.Clamp(r.Position ?? log.Count, 0, log.Count), item);
            return Results.Ok(new { item.Id });
        });

        g.MapPost("/pfl", async (FileRequest r, MediaLibrary library, StudioPlayers players) =>
        {
            if (await LoadAsync(r.Path, library) is not { } media) return Results.BadRequest("Geen afspeelbaar audiobestand.");
            players.Pfl.Load(media);
            players.Pfl.Play();
            return Results.Ok(new { media.Title });
        });
    }

    /// <summary>The library item of the file, or the file analysed on the spot; null when it is not a readable audio file.</summary>
    public static async Task<MediaItem?> LoadAsync(string? path, MediaLibrary library)
    {
        if (string.IsNullOrWhiteSpace(path) || !FileBrowser.IsAudio(path) || !File.Exists(path)) return null;
        if (library.FindByPath(path) is { } known) return known;
        try
        {
            var m = await Task.Run(() => library.Load(path, MediaKind.Song));
            if (m.Duration <= 0) return null;
            return m.Duration < JingleSeconds ? m with { Kind = MediaKind.Jingle } : m;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }
}
