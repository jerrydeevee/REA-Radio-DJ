using System.Globalization;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Ads;
using ReaRadioDj.Core.Library;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Server;

/// <summary>Commercials: campaigns, ad breaks, a dry run of a break and the proof of broadcast (list and CSV).</summary>
public static class AdsApi
{
    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/ads");

        g.MapGet("/campaigns", (AdStore store, AdService ads, MediaLibrary library) =>
            store.Campaigns().Select(c => Dto(c, ads, library)));
        g.MapPost("/campaigns", (AdCampaignDto d, AdStore store, AdService ads, MediaLibrary library) =>
        {
            if (string.IsNullOrWhiteSpace(d.Advertiser)) return Results.BadRequest(new { error = "vul een adverteerder in" });
            if (!DateOnly.TryParse(d.Start, CultureInfo.InvariantCulture, out var start) || !DateOnly.TryParse(d.End, CultureInfo.InvariantCulture, out var end))
                return Results.BadRequest(new { error = "begin- en einddatum als jjjj-mm-dd" });
            var c = new AdCampaign(d.Id, d.Advertiser, d.Name, d.Enabled, start, end, d.Days, d.Hours, d.PerDay, d.PerHour,
                d.Position switch { "first" => AdPosition.First, "last" => AdPosition.Last, _ => AdPosition.Any },
                d.Spots.Select(s => s.MediaId).Where(id => library.Get(id) is not null).ToList(), d.Notes ?? "");
            long id = store.Save(c);
            return Results.Ok(Dto(store.Campaign(id)!, ads, library));
        });
        g.MapDelete("/campaigns/{id:long}", (long id, AdStore store) => store.DeleteCampaign(id) ? Results.Ok() : Results.NotFound());

        g.MapGet("/breaks", (AdStore store) => store.Breaks().Select(Dto));
        g.MapPost("/breaks", (AdBreakDto d, AdStore store) =>
        {
            long id = store.Save(new AdBreak(d.Id, d.Name, d.Enabled, d.Days, d.Hours, d.Minute, d.Second,
                d.Timing == "soft" ? EventTiming.Soft : EventTiming.Hard, d.OpenerId, d.CloserId, d.MaxSeconds));
            return Results.Ok(Dto(store.Break(id)!));
        });
        g.MapDelete("/breaks/{id:long}", (long id, AdStore store) => store.DeleteBreak(id) ? Results.Ok() : Results.NotFound());
        // What the break would hold at its next time (or now).
        g.MapGet("/breaks/{id:long}/preview", (long id, AdStore store, AdService ads) =>
            ads.Preview(id, store.Break(id)?.ToEvent().NextOccurrence(DateTime.Now) ?? DateTime.Now));

        g.MapGet("/plays", (AdStore store, string? from, string? to, long? campaign, string? advertiser) =>
        {
            var (f, t) = Range(from, to);
            return store.Plays(f, t, campaign, advertiser).Select(p => new AdPlayDto(p.Id, p.Advertiser, p.Campaign, p.Spot, p.Break, p.Scheduled, p.Started,
                p.Length, p.Played, p.Complete));
        });
        g.MapGet("/proof.csv", (AdStore store, string? from, string? to, long? campaign, string? advertiser) =>
        {
            var (f, t) = Range(from, to);
            var csv = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(AdStore.ProofCsv(store.Plays(f, t, campaign, advertiser)))).ToArray();
            return Results.File(csv, "text/csv", $"uitzendbewijs-{f:yyyy-MM-dd}-{t.AddDays(-1):yyyy-MM-dd}.csv");
        });
        // The playout report: totals per advertiser and campaign.
        g.MapGet("/report", (AdStore store, string? from, string? to, long? campaign, string? advertiser) =>
        {
            var (f, t) = Range(from, to);
            return AdStore.Report(store.Plays(f, t, campaign, advertiser), DateTime.Now);
        });
        g.MapGet("/report.csv", (AdStore store, string? from, string? to, long? campaign, string? advertiser) =>
        {
            var (f, t) = Range(from, to);
            var csv = System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(
                AdStore.ReportCsv(AdStore.Report(store.Plays(f, t, campaign, advertiser), DateTime.Now)))).ToArray();
            return Results.File(csv, "text/csv", $"reclamerapport-{f:yyyy-MM-dd}-{t.AddDays(-1):yyyy-MM-dd}.csv");
        });
        // Everything in one file, for a later link with bookkeeping or a customer portal.
        g.MapGet("/proof.json", (AdStore store, ReaRadioDjOptions options, string? from, string? to, long? campaign, string? advertiser) =>
        {
            var (f, t) = Range(from, to);
            var plays = store.Plays(f, t, campaign, advertiser);
            var doc = new
            {
                station = options.StationName,
                from = f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to = t.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                advertiser = string.IsNullOrWhiteSpace(advertiser) ? null : advertiser,
                created = DateTime.Now,
                summary = AdStore.Report(plays, DateTime.Now),
                plays = plays.Select(p => new { p.Advertiser, p.Campaign, p.Spot, p.Break, p.Scheduled, p.Started, p.Length, p.Played, p.Complete }),
            };
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(doc, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
            return Results.File(bytes, "application/json", $"uitzendbewijs-{f:yyyy-MM-dd}-{t.AddDays(-1):yyyy-MM-dd}.json");
        });
    }

    /// <summary>From and to as dates (to inclusive); default the current month.</summary>
    private static (DateTime From, DateTime To) Range(string? from, string? to)
    {
        var today = DateTime.Today;
        var f = DateTime.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var a) ? a.Date : new DateTime(today.Year, today.Month, 1);
        var t = DateTime.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.None, out var b) ? b.Date : today;
        return (f, t.AddDays(1));
    }

    private static AdCampaignDto Dto(AdCampaign c, AdService ads, MediaLibrary library)
    {
        var (today, status) = ads.Progress(c, DateTime.Now);
        return new AdCampaignDto(c.Id, c.Advertiser, c.Name, c.Enabled, c.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            c.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.Days, c.Hours, c.PerDay, c.PerHour,
            c.Position switch { AdPosition.First => "first", AdPosition.Last => "last", _ => "any" },
            c.Spots.Select(id => library.Get(id) is { } m ? new AdSpotDto(id, m.DisplayName, Math.Round(m.CueOut - m.CueIn, 1)) : null)
                .OfType<AdSpotDto>().ToList(), c.Notes, today, status);
    }

    private static AdBreakDto Dto(AdBreak b)
    {
        var e = b.ToEvent();
        return new AdBreakDto(b.Id, b.Name, b.Enabled, b.Days, b.Hours, b.Minute, b.Second, b.Timing == EventTiming.Soft ? "soft" : "hard",
            b.OpenerId, b.CloserId, b.MaxSeconds, e.Describe(), b.Enabled ? e.NextOccurrence(DateTime.Now) : null);
    }
}
