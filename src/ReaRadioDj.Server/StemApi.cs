using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>Stems for the DJ decks: the state of the queue and a selection of the library to make stems of beforehand.</summary>
public static class StemApi
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/stems", (StemLibrary stems) => Results.Ok(Dto(stems)));

        api.MapPost("/stems", (StemRequest request, StemLibrary stems, MediaLibrary library) =>
        {
            if (!stems.Available) return Results.BadRequest("het scheidingsmodel ontbreekt (data\\models\\htdemucs.onnx)");
            int asked = 0;
            foreach (var id in request.Ids.Distinct().Take(5000))
                if (library.Get(id) is { } media)
                {
                    var file = MediaPaths.Resolve(media.Path);
                    if (File.Exists(file)) { stems.Request(file); asked++; }
                }
            return Results.Ok(Dto(stems) with { Waiting = stems.Queue().Count });
        });
    }

    public static StemsDto Dto(StemLibrary stems)
    {
        var queue = stems.Queue();
        string? working = queue.FirstOrDefault(q => stems.Status(q).State == StemState.Working);
        double progress = working is null ? 0 : stems.Status(working).Progress;
        const double gb = 1024.0 * 1024 * 1024;
        return new StemsDto(stems.Available, queue.Count, working is null ? null : Path.GetFileName(working), Math.Round(progress, 3),
            Math.Round(stems.Size() / gb, 2), Math.Round(stems.MaxBytes / gb, 1));
    }
}
