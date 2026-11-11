using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Model;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Server;

/// <summary>Title updates and the program log as text: settings made in the studio, stored in the database.</summary>
public static class TitleLogApi
{
    public const string Key = "title-log";

    public static OutputSettings Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return new();
        try { return JsonSerializer.Deserialize<OutputSettings>(json, JsonSerializerOptions.Web) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static TitleLogDto Dto(OutputSettings s, PlayoutOutputs o) => new(s.TitleFormat, [.. s.TitleKinds], s.TitleOther, s.TitleFile,
        s.TitleUrl, s.LogFolder, s.LogFormat, s.LogKeepDays, o.LastTitle, o.LogFile(DateTime.Now));

    public static OutputSettings From(TitleLogDto d) => new()
    {
        TitleFormat = string.IsNullOrWhiteSpace(d.TitleFormat) ? "{artiest} - {titel}" : d.TitleFormat.Trim(),
        TitleKinds = [.. d.TitleKinds.Where(k => OutputSettings.Kinds.Contains(k)).Distinct()],
        TitleOther = d.TitleOther.Trim(),
        TitleFile = d.TitleFile.Trim(),
        TitleUrl = d.TitleUrl.Trim(),
        LogFolder = d.LogFolder.Trim(),
        LogFormat = string.IsNullOrWhiteSpace(d.LogFormat) ? new OutputSettings().LogFormat : d.LogFormat,
        LogKeepDays = Math.Clamp(d.LogKeepDays, 0, 3650),
    };

    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        api.MapGet("/title-log", (PlayoutOutputs o) => Dto(o.Settings, o));
        api.MapPut("/title-log", (TitleLogDto body, PlayoutOutputs o, StationDb db) =>
        {
            if (body.TitleUrl.Trim() is { Length: > 0 } url && !(Uri.TryCreate(url.Replace("{tekst}", "x").Replace("{text}", "x"), UriKind.Absolute, out var u)
                && u.Scheme is "http" or "https"))
                return Results.BadRequest(new { error = "Het webadres moet met http:// of https:// beginnen." });
            var s = From(body);
            o.Settings = s;
            db.SetSetting(Key, JsonSerializer.Serialize(s, JsonSerializerOptions.Web));
            return Results.Ok(Dto(s, o));
        });
        // A test with a made-up song: shows the title and the log line, and sends the title to the file and web address.
        api.MapPost("/title-log/test", async (PlayoutOutputs o) =>
        {
            var media = new MediaItem { Path = "", Kind = MediaKind.Song, Artist = "Testartiest", Title = "Testnummer", Category = "Test" };
            var item = new OutputItem(DateTime.Now, "song", "test", media, 213, options.StationName);
            string title = o.TitleFor(item) ?? "";
            string line = PlayoutOutputs.Fill(o.Settings.LogFormat, item);
            bool? url = string.IsNullOrWhiteSpace(o.Settings.TitleUrl) ? null : await o.CallUrlAsync(o.Settings.TitleUrl, title);
            return Results.Ok(new TitleLogTestDto(title, line, url));
        });
    }
}
