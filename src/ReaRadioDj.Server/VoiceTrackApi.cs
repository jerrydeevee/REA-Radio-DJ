using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Server;

/// <summary>Voicetracking from the studio: start, next, stop, then listen back, redo or place in the log.</summary>
public static class VoiceTrackApi
{
    public static VoiceTrackDto Dto(VoiceTracker t, string? error = null) => new(
        t.Step.ToString().ToLowerInvariant(), Math.Round(t.Elapsed, 2), t.OutroLength, t.SlotBefore,
        t.Take is { } k ? new VoiceTakeDto(k.Id, k.Length, k.StartEarly, k.NextAt, k.BeforeItemId, k.Previous, k.Next, k.PeakDb) : null,
        error);

    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        var vt = api.MapGroup("/voicetrack");
        VoiceTracker.CleanUp(Path.Combine(options.DataFolder, "voicetracks"));

        vt.MapGet("", (VoiceTracker t) => Dto(t));
        vt.MapPost("/start", (VoiceTrackStartRequest r, VoiceTracker t, OwnVoiceRecorder own) => own.Recording
            ? Dto(t, "er loopt een stemopname (Mijn stem); probeer het straks")
            : Try(t, () => t.Start(r.BeforeId, Math.Clamp(r.Preroll, 0, 30), r.Device)));
        vt.MapPost("/next", (VoiceTracker t) => Try(t, t.Next));
        vt.MapPost("/stop", (VoiceTracker t) => Try(t, () => t.Stop()));
        vt.MapPost("/cancel", (VoiceTracker t) => Try(t, t.Cancel));
        vt.MapPost("/take/play", (VoiceTracker t) => Try(t, t.PlayTake));
        vt.MapPost("/take/place", (VoiceTracker t) => Try(t, () => t.Place()));
        vt.MapDelete("/take", (VoiceTracker t) => Try(t, t.DiscardTake));

        // From another place (the /voicetrack/ page of a presenter at home). These are POSTs, so from the network they
        // need a signed-in user or the control password, also the audio of the songs.
        var remote = vt.MapGroup("/remote");
        remote.MapPost("/slot/{beforeId:long}", (long beforeId, VoiceTracker t) =>
        {
            try
            {
                var (prev, next, before) = t.Slot(beforeId);
                return Results.Ok(new RemoteSlotDto(before.Id, Side(prev), Side(next) ?? new RemoteSideDto(before.Label, 0, 0, 0, false)));
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
        });
        remote.MapPost("/audio/{beforeId:long}/{which}", (long beforeId, string which, VoiceTracker t) =>
        {
            try
            {
                var (prev, next, _) = t.Slot(beforeId);
                var media = which == "previous" ? prev : which == "next" ? next : null;
                if (media is null || !File.Exists(media.Path)) return Results.NotFound("geen audio voor dit item");
                return Results.File(media.Path, ContentType(media.Path), enableRangeProcessing: true);
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(ex.Message); }
        });
        remote.MapPost("/{beforeId:long}", async (long beforeId, double? outro, double? next, HttpRequest request, VoiceTracker t, MessageCenter messages) =>
        {
            using var body = new MemoryStream();
            await request.Body.CopyToAsync(body);
            if (body.Length > MaxUpload) return Results.BadRequest("de opname is te lang (hooguit 3 minuten)");
            try
            {
                var item = t.PlaceRecording(body.ToArray(), beforeId, outro ?? 0, next);
                messages.Info($"Voicetrack van op afstand geplaatst ({item.Media!.Duration:0.0} s)");
                return Results.Ok(new RemotePlacedDto(item.Id, item.Media.Duration, item.StartEarly, next is null ? null : item.Media.MixPoint));
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or BassException or IOException)
            {
                return Results.BadRequest(ex.Message);
            }
        });
    }

    /// <summary>Three minutes of 16-bit stereo at 48 kHz, with room to spare.</summary>
    private const long MaxUpload = 40L * 1024 * 1024;

    private static RemoteSideDto? Side(ReaRadioDj.Core.Model.MediaItem? m) => m is null ? null
        : new RemoteSideDto(m.DisplayName, m.CueIn, m.MixPoint, m.Duration, File.Exists(m.Path));

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".flac" => "audio/flac",
        ".ogg" or ".oga" => "audio/ogg",
        ".opus" => "audio/ogg",
        ".m4a" or ".mp4" or ".aac" => "audio/mp4",
        _ => "application/octet-stream",
    };

    private static VoiceTrackDto Try(VoiceTracker t, Action action)
    {
        try
        {
            action();
            return Dto(t);
        }
        catch (Exception ex) when (ex is InvalidOperationException or BassException or IOException)
        {
            return Dto(t, ex.Message);
        }
    }
}

/// <summary>A slot for a voice track recorded somewhere else: the item before it (its outro) and the one after it.</summary>
public sealed record RemoteSlotDto(long BeforeId, RemoteSideDto? Previous, RemoteSideDto Next);
public sealed record RemoteSideDto(string Label, double CueIn, double MixPoint, double Duration, bool Audio);
public sealed record RemotePlacedDto(long Id, double Length, double StartEarly, double? NextAt);
