using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>Replacement locations for audio files, and moving the library for good.</summary>
public static class PathsApi
{
    public const string Key = "path-substitutes";

    public static IReadOnlyList<PathSubstitute> Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return [];
        try { return JsonSerializer.Deserialize<List<PathSubstitute>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }

    private static List<PathSubstituteDto> Dtos() =>
        [.. MediaPaths.Rules.Select(r => new PathSubstituteDto(r.From, r.To, r.Enabled, Directory.Exists(r.To)))];

    public static void Map(RouteGroupBuilder api, StationDb db)
    {
        MediaPaths.Rules = Load(db);
        api.MapGet("/library/substitutes", Dtos);
        api.MapPut("/library/substitutes", (List<PathSubstituteDto> body) =>
        {
            MediaPaths.Rules = [.. body.Select(d => new PathSubstitute(d.From, d.To, d.Enabled))];
            db.SetSetting(Key, JsonSerializer.Serialize(MediaPaths.Rules, JsonSerializerOptions.Web));
            return Dtos();
        });
        // Moves the stored paths for good (the music moved to another drive or server).
        api.MapPost("/library/relocate", (RelocateRequest r, MediaLibrary library) =>
        {
            if (string.IsNullOrWhiteSpace(r.From) || string.IsNullOrWhiteSpace(r.To))
                return Results.BadRequest(new { error = "Vul de oude en de nieuwe map in." });
            var (changed, found) = library.Relocate(r.From, r.To);
            return Results.Ok(new RelocateResultDto(changed, found));
        });
    }
}
