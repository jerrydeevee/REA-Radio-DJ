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
        vt.MapPost("/start", (VoiceTrackStartRequest r, VoiceTracker t) =>
            Try(t, () => t.Start(r.BeforeId, Math.Clamp(r.Preroll, 0, 30), r.Device)));
        vt.MapPost("/next", (VoiceTracker t) => Try(t, t.Next));
        vt.MapPost("/stop", (VoiceTracker t) => Try(t, () => t.Stop()));
        vt.MapPost("/cancel", (VoiceTracker t) => Try(t, t.Cancel));
        vt.MapPost("/take/play", (VoiceTracker t) => Try(t, t.PlayTake));
        vt.MapPost("/take/place", (VoiceTracker t) => Try(t, () => t.Place()));
        vt.MapDelete("/take", (VoiceTracker t) => Try(t, t.DiscardTake));
    }

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
