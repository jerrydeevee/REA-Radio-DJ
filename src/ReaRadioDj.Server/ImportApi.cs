using System.Globalization;
using System.Text;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Import;
using ReaRadioDj.Core.Library;

namespace ReaRadioDj.Server;

/// <summary>
/// External music scheduling: import profiles, reading a log file (dry run and for real), the imported hours, and the
/// library as CSV for the scheduler. An imported hour plays its lines instead of the clock of that hour.
/// </summary>
public static class ImportApi
{
    private const int MaxLinesShown = 2000;

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/import");

        g.MapGet("/profiles", (ImportStore store) => store.Profiles().Select(Dto));
        g.MapPost("/profiles", (ImportProfileDto d, ImportStore store) =>
        {
            var p = FromDto(d);
            if (p.Problem() is { } problem) return Results.BadRequest(new { error = problem });
            long id = store.Save(p);
            return Results.Ok(Dto(store.Profile(id)!));
        });
        g.MapDelete("/profiles/{id:long}", (long id, ImportStore store) => store.DeleteProfile(id) ? Results.Ok() : Results.NotFound());

        g.MapPost("/preview", (ImportRequestDto r, ImportStore store, MediaLibrary library) => Read(r, store, library, apply: null));
        g.MapPost("/apply", (ImportRequestDto r, ImportStore store, MediaLibrary library, AutomationService automation, MessageCenter messages) =>
            Read(r, store, library, apply: hours =>
            {
                automation.Replan();
                messages.Info($"Log geïmporteerd: {hours.Count} uur ({r.FileName ?? "tekst"})");
            }));

        g.MapGet("/hours", (ImportStore store) =>
        {
            var now = DateTime.Now;
            return store.Hours(new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0)).Select(h =>
                new ImportedHourDto(h.Hour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), h.Hour.Hour, h.Items, h.Source));
        });
        g.MapDelete("/hours", (string date, int? hour, ImportStore store, AutomationService automation) =>
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                return Results.BadRequest(new { error = "datum als jjjj-mm-dd" });
            int n = store.Delete(day, hour);
            automation.Replan();
            return Results.Ok(new { removed = n });
        });

        api.MapGet("/library/export.csv", (MediaLibrary library, string? sep) =>
        {
            var csv = LibraryExport.Csv(library.Search(null, limit: int.MaxValue), string.IsNullOrEmpty(sep) ? ";" : sep);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return Results.File(bytes, "text/csv", $"bibliotheek-{DateTime.Today:yyyy-MM-dd}.csv");
        });
    }

    private static IResult Read(ImportRequestDto r, ImportStore store, MediaLibrary library, Action<IReadOnlyList<DateTime>>? apply)
    {
        if (store.Profile(r.ProfileId) is not { } profile) return Results.BadRequest(new { error = "kies een importprofiel" });
        if (!DateOnly.TryParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Results.BadRequest(new { error = "datum als jjjj-mm-dd" });
        if (string.IsNullOrWhiteSpace(r.Text)) return Results.BadRequest(new { error = "het bestand is leeg" });

        var now = DateTime.Now;
        var lines = LogImporter.Read(profile, r.Text, date, r.StartHour, new MediaIndex(library.Search(null, limit: int.MaxValue)), now);
        var hours = lines.Where(l => l.Media is not null && l.Hour is not null).Select(l => l.Hour!.Value).Distinct().Order().ToList();
        bool applied = false;
        string? error = null;
        if (apply is not null)
        {
            if (hours.Count == 0) error = "geen enkele regel gevonden in de bibliotheek: er is niets geïmporteerd";
            else
            {
                string source = $"{profile.Name}: {r.FileName ?? "tekst"}";
                store.Replace(lines, source, now);
                apply(hours);
                applied = true;
            }
        }
        if (hours.Any(h => h.AddHours(1) <= now) && error is null)
            error = "let op: een deel van de regels is voor een uur dat al voorbij is";
        return Results.Ok(new ImportResultDto(applied, lines.Count, lines.Count(l => l.Media is not null),
            lines.Take(MaxLinesShown).Select(l => new ImportLineDto(l.Number, l.Hour?.ToString("ddd d MMM HH:00", new CultureInfo("nl-NL")),
                l.Text, l.Media?.DisplayName, l.Problem)).ToList(),
            hours.Select(h => h.ToString("ddd d MMM HH:00", new CultureInfo("nl-NL"))).ToList(), error));
    }

    private static ImportColumnDto Dto(ImportColumn c) => new(c.Column, c.Start, c.Length);
    private static ImportColumn FromDto(ImportColumnDto? c) => c is null ? ImportColumn.None : new(Math.Max(0, c.Column), Math.Max(0, c.Start), Math.Max(0, c.Length));

    private static ImportProfileDto Dto(ImportProfile p) => new(p.Id, p.Name, p.Layout == ImportLayout.FixedWidth ? "fixed" : "delimited",
        p.Separator, p.HasHeader, p.SkipPrefix, Dto(p.TimeAt), p.TimeFormat, Dto(p.FileAt), Dto(p.IdAt), Dto(p.ArtistAt), Dto(p.TitleAt),
        p.PathFrom, p.PathTo);

    private static ImportProfile FromDto(ImportProfileDto d) => new ImportProfile(d.Id, d.Name,
        d.Layout == "fixed" ? ImportLayout.FixedWidth : ImportLayout.Delimited, d.Separator, d.HasHeader, d.SkipPrefix ?? "",
        FromDto(d.Time), d.TimeFormat, FromDto(d.File), FromDto(d.MediaId), FromDto(d.Artist), FromDto(d.Title), d.PathFrom ?? "", d.PathTo ?? "").Checked();
}
