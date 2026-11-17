using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;

namespace ReaRadioDj.Server;

/// <summary>The Recordings window: emergency recorder and scheduled recordings.</summary>
public static class ExtraRecordingsApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/recordings/extra", (ScheduledRecordingService s, AirRecorder air) => Dto(s, air));

        api.MapPut("/recordings/scheduled", (List<ScheduledRecordingDto> items, ScheduledRecordingService s, AirRecorder air) =>
        {
            s.Save(items.Select(d => new ScheduledRecording(d.Id ?? "", d.Name ?? "", d.Enabled, d.Days, d.Start ?? "", d.Minutes, d.Source ?? "",
                d.Replace, d.Artist ?? "")));
            return Dto(s, air);
        });

        api.MapPost("/recordings/scheduled/{id}/start", (string id, ScheduledRecordingService s, AirRecorder air) =>
            s.StartNow(id) is { } error ? Results.BadRequest(error) : Results.Ok(Dto(s, air)));

        api.MapPost("/recordings/scheduled/{id}/stop", (string id, ScheduledRecordingService s, AirRecorder air) =>
            s.StopNow(id) is { } error ? Results.BadRequest(error) : Results.Ok(Dto(s, air)));

        api.MapPost("/recordings/emergency/{action}", (string action, ScheduledRecordingService s, AirRecorder air) =>
        {
            string? error = action.ToLowerInvariant() switch
            {
                "start" => s.StartEmergency(),
                "stop" => Stop(s),
                _ => s.ToggleEmergency(),
            };
            return error is null ? Results.Ok(Dto(s, air)) : Results.BadRequest(error);
        });
    }

    private static string? Stop(ScheduledRecordingService s) { s.StopEmergency(); return null; }

    private static ExtraRecordingsDto Dto(ScheduledRecordingService s, AirRecorder air)
    {
        var e = s.Emergency();
        var now = DateTime.Now;
        return new ExtraRecordingsDto(
            new EmergencyRecordingDto(e.Recording, e.Started, Math.Round(e.Seconds), e.Channels),
            s.Status(now).Select(x => new ScheduledRecordingStatusDto(
                new ScheduledRecordingDto(x.Item.Id, x.Item.Name, x.Item.Enabled, x.Item.Days, x.Item.Start, x.Item.Minutes, x.Item.Source, x.Item.Replace, x.Item.Artist),
                x.Next, x.Recording, x.Until, Math.Round(x.Seconds), x.Last?.Time, x.Last?.File, x.Last?.Seconds ?? 0, x.Last?.Error, x.Problem)).ToList(),
            s.Sources().Select(x => new RecordingSourceDto(x.Id, x.Name)).ToList(),
            air.Folder);
    }
}
