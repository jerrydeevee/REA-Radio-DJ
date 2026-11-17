using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Ads;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Server;

/// <summary>
/// Keeps an eye on the placeholders: a warning a day before the sound must be there, an alarm when it is late, and a
/// note when it has arrived. The sound arrives by putting the file in the placeholder folder under the name shown,
/// or by sending it from the studio.
/// </summary>
public sealed class PlaceholderService(PlaceholderStore store, MediaLibrary library, AdStore ads, ReaRadioDjOptions options,
    MessageCenter messages, ILogger<PlaceholderService> logger) : BackgroundService
{
    /// <summary>Where the sound of the placeholders goes; it is a library folder (jingles, category Plaatshouders).</summary>
    public string Folder => Path.GetFullPath(Path.Combine(options.DataFolder, "Plaatshouders"));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            do
            {
                try { Check(DateTime.Now); }
                catch (Exception ex) { logger.LogError(ex, "Placeholder check failed"); }
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    internal void Check(DateTime now)
    {
        foreach (var p in store.List())
        {
            if (p.Arrived)
            {
                store.Forget(p.MediaId);
                messages.Info($"Het geluid van plaatshouder {p.DisplayName} is binnen; het item speelt nu gewoon.");
                continue;
            }
            switch (p.WarningDue(now, PlaceholderStore.WarnBefore))
            {
                case 1:
                    store.MarkWarned(p.MediaId, 1);
                    messages.Add("waarschuwing", $"Plaatshouder {p.DisplayName}: het geluid moet er {p.Due:ddd d MMM HH:mm} zijn en ontbreekt nog.", "plaatshouder");
                    break;
                case 2:
                    store.MarkWarned(p.MediaId, 2);
                    messages.Add("alarm", $"Plaatshouder {p.DisplayName} is er niet op tijd (moest er {p.Due:ddd d MMM HH:mm} zijn). Hij wordt overgeslagen tot het geluid er is.", "plaatshouder");
                    break;
            }
        }
    }

    /// <summary>Makes the placeholder folder a library folder once.</summary>
    private string? EnsureFolder()
    {
        lock (this)
        {
            Directory.CreateDirectory(Folder);
            bool Known() => library.Folders().Any(f => string.Equals(Path.GetFullPath(f.Path).TrimEnd('\\'), Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (Known()) return null;
            var (_, error) = library.AddFolder(Folder, MediaKind.Jingle, "Plaatshouders", watch: true, writeTags: false);
            return error is null || Known() ? null : $"De map voor plaatshouders staat niet in de bibliotheek: {error}";
        }
    }

    public (long Id, string? Error) Create(PlaceholderRequest r)
    {
        if (EnsureFolder() is { } error) return (0, error);
        var kind = r.Song ? MediaKind.Song : MediaKind.Jingle;
        var (id, err) = store.Create(Folder, kind, r.CategoryId, r.Artist ?? "", r.Title ?? "", r.Seconds, r.Due, r.Note ?? "");
        if (err is not null) return (0, err);
        if (r.CampaignId is long cid && ads.Campaign(cid) is { } c && !c.Spots.Contains(id))
            ads.Save(c with { Spots = [.. c.Spots, id] });
        logger.LogInformation("Placeholder {Id} made: {Artist} - {Title}, due {Due}", id, r.Artist, r.Title, r.Due);
        Check(DateTime.Now);
        return (id, null);
    }

    public bool Delete(long id)
    {
        if (!store.Delete(id)) return false;
        foreach (var c in ads.Campaigns().Where(c => c.Spots.Contains(id)))
            ads.Save(c with { Spots = c.Spots.Where(s => s != id).ToList() });
        return true;
    }

    /// <summary>Puts the sent sound in place of the placeholder (an MP3 as it is, anything else converted) and scans it in.</summary>
    public async Task<string?> DeliverAsync(long id, Stream body, string fileName)
    {
        if (store.Get(id) is not { } p) return "plaatshouder niet gevonden";
        if (p.Arrived) return "het geluid is er al";
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var tmp = Path.Combine(Path.GetTempPath(), $"rea-placeholder-{id}-{Guid.NewGuid():N}{(ext.Length > 0 ? ext : ".bin")}");
        try
        {
            await using (var f = File.Create(tmp)) await body.CopyToAsync(f);
            var measured = RecordingCutter.Measure(tmp);
            if (measured is null) return "dat bestand is geen geluid dat REA-Radio-DJ kan lezen";
            Directory.CreateDirectory(Path.GetDirectoryName(p.Path)!);
            if (ext == ".mp3") File.Copy(tmp, p.Path, overwrite: false);
            else RecordingCutter.Cut([new CutPiece(tmp, 0, measured.Seconds)], p.Path, 256);
            try
            {
                using var t = TagLib.File.Create(p.Path);
                // The item keeps the name it was planned under, whatever the delivered file says.
                t.Tag.Title = p.Title; t.Tag.Performers = p.Artist.Length > 0 ? [p.Artist] : []; t.Save();
            }
            catch (Exception ex) { logger.LogWarning(ex, "Could not tag {File}", p.Path); }
            await library.ScanAsync();
            Check(DateTime.Now);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BassException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Placeholder {Id}: sound not delivered", id);
            return ex.Message;
        }
        finally
        {
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public PlaceholdersDto Dto(DateTime now) => new(store.List().Select(p => new PlaceholderDto(p.MediaId, p.Artist, p.Title, Path.GetFileName(p.Path),
            p.Seconds, p.Due, p.Note, p.Arrived, !p.Arrived && now >= p.Due, !p.Arrived && now >= p.Due - PlaceholderStore.WarnBefore,
            ads.Campaigns().Where(c => c.Spots.Contains(p.MediaId)).Select(c => $"{c.Advertiser} – {c.Name}").ToList())).ToList(),
        Folder,
        ads.Campaigns().Select(c => new PlaceholderCampaignDto(c.Id, $"{c.Advertiser} – {c.Name}")).ToList());

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/placeholders", (PlaceholderService s) => s.Dto(DateTime.Now));
        api.MapPost("/placeholders", (PlaceholderRequest r, PlaceholderService s) =>
        {
            var (_, error) = s.Create(r);
            return error is null ? Results.Ok(s.Dto(DateTime.Now)) : Results.BadRequest(error);
        });
        api.MapPut("/placeholders/{id:long}", (long id, PlaceholderUpdate r, PlaceholderService s, PlaceholderStore st) =>
            st.Update(id, r.Due, r.Note ?? "") ? Results.Ok(s.Dto(DateTime.Now)) : Results.NotFound());
        api.MapDelete("/placeholders/{id:long}", (long id, PlaceholderService s) =>
            s.Delete(id) ? Results.Ok(s.Dto(DateTime.Now)) : Results.BadRequest("alleen een plaatshouder zonder geluid kan weg"));
        api.MapPost("/placeholders/{id:long}/audio", async (long id, string? name, HttpRequest request, PlaceholderService s) =>
            await s.DeliverAsync(id, request.Body, name ?? "geluid.mp3") is { } error ? Results.BadRequest(error) : Results.Ok(s.Dto(DateTime.Now)));
    }
}
