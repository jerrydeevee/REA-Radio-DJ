using System.Globalization;
using System.Text;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Core.Ads;

/// <summary>Where in an ad break a campaign wants its spot.</summary>
public enum AdPosition { Any = 0, First = 1, Last = 2 }

/// <summary>
/// An advertiser's campaign: from <see cref="Start"/> to <see cref="End"/> (inclusive), on the given days and hours,
/// <see cref="PerDay"/> plays a day and at most <see cref="PerHour"/> an hour. Its spots take turns.
/// </summary>
public sealed record AdCampaign(long Id, string Advertiser, string Name, bool Enabled, DateOnly Start, DateOnly End, int Days, string Hours,
    int PerDay, int PerHour, AdPosition Position, IReadOnlyList<long> Spots, string Notes = "")
{
    public bool Runs(DateTime at) => Enabled && Spots.Count > 0
        && DateOnly.FromDateTime(at) >= Start && DateOnly.FromDateTime(at) <= End
        && (Days & TimedEvent.DayBit(at.DayOfWeek)) != 0 && HourSet.Parse(Hours).Contains(at.Hour);

    public AdCampaign Checked() => this with
    {
        Advertiser = Advertiser.Trim(),
        Name = string.IsNullOrWhiteSpace(Name) ? Advertiser.Trim() : Name.Trim(),
        End = End < Start ? Start : End,
        Days = Days & TimedEvent.AllDays,
        Hours = string.IsNullOrWhiteSpace(Hours) ? "*" : Hours.Trim(),
        PerDay = Math.Clamp(PerDay, 1, 200),
        PerHour = Math.Clamp(PerHour, 1, 12),
        Spots = Spots.Distinct().ToList(),
    };
}

/// <summary>An ad break on a fixed time: opener, spots up to <see cref="MaxSeconds"/>, closer.</summary>
public sealed record AdBreak(long Id, string Name, bool Enabled, int Days, string Hours, int Minute, int Second, EventTiming Timing,
    long? OpenerId, long? CloserId, int MaxSeconds)
{
    /// <summary>As a fixed-time event (negative id, a block of its own beside the news blocks).</summary>
    public TimedEvent ToEvent() => new(-1_000_000 - Id, Name, Enabled, Days, Hours, Minute, Second,
        Timing == EventTiming.Soft ? EventTiming.Soft : EventTiming.Hard, EventAction.Ads, null, Id);

    public AdBreak Checked() => this with
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "Reclame" : Name.Trim(),
        Days = Days & TimedEvent.AllDays,
        Hours = string.IsNullOrWhiteSpace(Hours) ? "*" : Hours.Trim(),
        Minute = Math.Clamp(Minute, 0, 59),
        Second = Math.Clamp(Second, 0, 59),
        Timing = Timing == EventTiming.Soft ? EventTiming.Soft : EventTiming.Hard,
        MaxSeconds = Math.Clamp(MaxSeconds, 10, 900),
    };
}

/// <summary>One spot that went (or is going) on air, kept as proof of broadcast. Names are copied, so the proof stays
/// readable when a campaign is deleted.</summary>
public sealed record AdPlay(long Id, long CampaignId, long MediaId, string Advertiser, string Campaign, string Spot, string Break,
    DateTime Scheduled, DateTime? Started, double Length, double? Played)
{
    /// <summary>Played (nearly) to the end.</summary>
    public bool Complete => Played is double p && p >= Length - 1.0;
}

/// <summary>What the planner needs to know about a campaign's plays so far.</summary>
public sealed record AdCounts(int Today, int ThisHour, IReadOnlyDictionary<long, int> PerSpot);

public sealed record AdPick(AdCampaign Campaign, long MediaId, double Length);

/// <summary>
/// Fills an ad break. Campaigns that run now and still have plays left (today and this hour) are ranked by how far
/// behind they are: plays still needed today divided by the breaks left today. One spot per advertiser per break;
/// the break never gets longer than its maximum. Within a campaign the least-played spot goes first.
/// </summary>
public static class AdPlanner
{
    public static IReadOnlyList<AdPick> Fill(IEnumerable<AdCampaign> campaigns, DateTime at, int maxSeconds,
        Func<AdCampaign, AdCounts> counts, Func<AdCampaign, int> breaksLeftToday, Func<long, double?> length)
    {
        var ranked = new List<(AdCampaign C, double Urgency, long Spot, double Length)>();
        foreach (var c in campaigns.Where(c => c.Runs(at)))
        {
            var n = counts(c);
            if (n.Today >= c.PerDay || n.ThisHour >= c.PerHour) continue;
            var spot = c.Spots
                .Select(id => (Id: id, Length: length(id)))
                .Where(s => s.Length is > 0)
                .OrderBy(s => n.PerSpot.TryGetValue(s.Id, out var played) ? played : 0)
                .ThenBy(s => c.Spots.ToList().IndexOf(s.Id))
                .FirstOrDefault();
            if (spot.Length is not double len) continue;
            double urgency = (c.PerDay - n.Today) / (double)Math.Max(1, breaksLeftToday(c));
            ranked.Add((c, urgency, spot.Id, len));
        }

        var chosen = new List<(AdCampaign C, double Urgency, long Spot, double Length)>();
        double total = 0;
        foreach (var r in ranked.OrderByDescending(r => r.Urgency).ThenBy(r => r.C.Id))
        {
            if (total + r.Length > maxSeconds) continue;
            if (chosen.Any(x => string.Equals(x.C.Advertiser, r.C.Advertiser, StringComparison.OrdinalIgnoreCase))) continue;
            chosen.Add(r);
            total += r.Length;
        }
        return chosen
            .OrderBy(r => r.C.Position switch { AdPosition.First => 0, AdPosition.Last => 2, _ => 1 })
            .ThenByDescending(r => r.Urgency)
            .Select(r => new AdPick(r.C, r.Spot, r.Length))
            .ToList();
    }

    /// <summary>How many times an ad break still comes today from <paramref name="at"/> on, in hours the campaign runs.</summary>
    public static int BreaksLeftToday(IEnumerable<AdBreak> breaks, AdCampaign c, DateTime at)
    {
        var hours = HourSet.Parse(c.Hours);
        int count = 0;
        foreach (var b in breaks.Where(b => b.Enabled))
        {
            var e = b.ToEvent();
            for (var t = e.NextOccurrence(at); t is DateTime x && x.Date == at.Date; t = e.NextOccurrence(x.AddSeconds(1)))
                if (hours.Contains(x.Hour)) count++;
        }
        return count;
    }
}

public sealed class AdStore(StationDb db)
{
    private readonly object _gate = new();
    private IReadOnlyList<AdCampaign>? _campaigns;
    private IReadOnlyList<AdBreak>? _breaks;

    // ------------------------------------------------------------------ campaigns

    public IReadOnlyList<AdCampaign> Campaigns()
    {
        lock (_gate)
        {
            if (_campaigns is not null) return _campaigns;
            var spots = db.Query("SELECT campaign_id, media_id FROM ad_spots ORDER BY campaign_id, position",
                r => (C: r.GetInt64(0), M: r.GetInt64(1))).GroupBy(x => x.C).ToDictionary(g => g.Key, g => (IReadOnlyList<long>)g.Select(x => x.M).ToList());
            return _campaigns = db.Query(
                "SELECT id, advertiser, name, enabled, start, \"end\", days, hours, per_day, per_hour, position, notes FROM ad_campaigns ORDER BY advertiser, name",
                r => new AdCampaign(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3) != 0, DateOnly.Parse(r.GetString(4), CultureInfo.InvariantCulture),
                    DateOnly.Parse(r.GetString(5), CultureInfo.InvariantCulture), (int)r.GetInt64(6), r.GetString(7), (int)r.GetInt64(8), (int)r.GetInt64(9),
                    (AdPosition)r.GetInt64(10), spots.GetValueOrDefault(r.GetInt64(0)) ?? [], r.GetString(11)));
        }
    }

    public AdCampaign? Campaign(long id) => Campaigns().FirstOrDefault(c => c.Id == id);

    public long Save(AdCampaign campaign)
    {
        var c = campaign.Checked();
        long id = c.Id;
        db.Transaction((conn, tx) =>
        {
            (string, object?)[] args =
            [
                ("$id", c.Id), ("$a", c.Advertiser), ("$n", c.Name), ("$e", c.Enabled ? 1 : 0), ("$s", Date(c.Start)), ("$t", Date(c.End)),
                ("$d", c.Days), ("$h", c.Hours), ("$pd", c.PerDay), ("$ph", c.PerHour), ("$p", (int)c.Position), ("$no", c.Notes),
            ];
            if (c.Id > 0)
            {
                using var upd = StationDb.Command(conn, """
                    UPDATE ad_campaigns SET advertiser=$a, name=$n, enabled=$e, start=$s, "end"=$t, days=$d, hours=$h, per_day=$pd, per_hour=$ph,
                        position=$p, notes=$no WHERE id=$id
                    """, args);
                upd.Transaction = tx;
                upd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = StationDb.Command(conn, """
                    INSERT INTO ad_campaigns(advertiser, name, enabled, start, "end", days, hours, per_day, per_hour, position, notes)
                    VALUES($a, $n, $e, $s, $t, $d, $h, $pd, $ph, $p, $no); SELECT last_insert_rowid();
                    """, args);
                cmd.Transaction = tx;
                id = (long)cmd.ExecuteScalar()!;
            }
            using (var del = StationDb.Command(conn, "DELETE FROM ad_spots WHERE campaign_id = $c", ("$c", id))) { del.Transaction = tx; del.ExecuteNonQuery(); }
            for (int i = 0; i < c.Spots.Count; i++)
            {
                using var ins = StationDb.Command(conn, "INSERT INTO ad_spots(campaign_id, media_id, position) VALUES($c, $m, $p)",
                    ("$c", id), ("$m", c.Spots[i]), ("$p", i));
                ins.Transaction = tx;
                ins.ExecuteNonQuery();
            }
        });
        lock (_gate) _campaigns = null;
        return id;
    }

    public bool DeleteCampaign(long id)
    {
        db.Execute("DELETE FROM ad_spots WHERE campaign_id = $id", ("$id", id));
        bool done = db.Execute("DELETE FROM ad_campaigns WHERE id = $id", ("$id", id)) > 0;
        lock (_gate) _campaigns = null;
        return done;
    }

    // ------------------------------------------------------------------ breaks

    public IReadOnlyList<AdBreak> Breaks()
    {
        lock (_gate)
            return _breaks ??= db.Query(
                "SELECT id, name, enabled, days, hours, minute, second, timing, opener_id, closer_id, max_seconds FROM ad_breaks ORDER BY minute, second, name",
                r => new AdBreak(r.GetInt64(0), r.GetString(1), r.GetInt64(2) != 0, (int)r.GetInt64(3), r.GetString(4), (int)r.GetInt64(5),
                    (int)r.GetInt64(6), (EventTiming)r.GetInt64(7), r.IsDBNull(8) ? null : r.GetInt64(8), r.IsDBNull(9) ? null : r.GetInt64(9),
                    (int)r.GetInt64(10)));
    }

    public AdBreak? Break(long id) => Breaks().FirstOrDefault(b => b.Id == id);

    public IReadOnlyList<TimedEvent> Events => Breaks().Where(b => b.Enabled).Select(b => b.ToEvent()).ToList();

    public long Save(AdBreak block)
    {
        var b = block.Checked();
        (string, object?)[] args =
        [
            ("$id", b.Id), ("$n", b.Name), ("$e", b.Enabled ? 1 : 0), ("$d", b.Days), ("$h", b.Hours), ("$m", b.Minute), ("$s", b.Second),
            ("$t", (int)b.Timing), ("$o", b.OpenerId), ("$c", b.CloserId), ("$max", b.MaxSeconds),
        ];
        long id = b.Id;
        if (b.Id > 0)
            db.Execute("UPDATE ad_breaks SET name=$n, enabled=$e, days=$d, hours=$h, minute=$m, second=$s, timing=$t, opener_id=$o, closer_id=$c, max_seconds=$max WHERE id=$id", args);
        else
            id = db.Insert("INSERT INTO ad_breaks(name, enabled, days, hours, minute, second, timing, opener_id, closer_id, max_seconds) VALUES($n, $e, $d, $h, $m, $s, $t, $o, $c, $max)", args);
        lock (_gate) _breaks = null;
        return id;
    }

    public bool DeleteBreak(long id)
    {
        bool done = db.Execute("DELETE FROM ad_breaks WHERE id = $id", ("$id", id)) > 0;
        lock (_gate) _breaks = null;
        return done;
    }

    // ------------------------------------------------------------------ proof of broadcast

    public long Planned(AdPick pick, string spot, string breakName, DateTime scheduled) => db.Insert("""
        INSERT INTO ad_plays(campaign_id, media_id, advertiser, campaign, spot, break, scheduled, length)
        VALUES($c, $m, $a, $n, $s, $b, $t, $l)
        """, ("$c", pick.Campaign.Id), ("$m", pick.MediaId), ("$a", pick.Campaign.Advertiser), ("$n", pick.Campaign.Name), ("$s", spot),
        ("$b", breakName), ("$t", StationDb.Iso(scheduled)), ("$l", Math.Round(pick.Length, 2)));

    public void Started(long playId, DateTime at) => db.Execute("UPDATE ad_plays SET started = $t WHERE id = $id", ("$t", StationDb.Iso(at)), ("$id", playId));

    public void Ended(long playId, double played) => db.Execute("UPDATE ad_plays SET played = $p WHERE id = $id", ("$p", Math.Round(played, 2)), ("$id", playId));

    /// <summary>Plays that were planned but never started within ten minutes of their time (the break was skipped or removed).</summary>
    public int ForgetMissed(DateTime now) => db.Execute("DELETE FROM ad_plays WHERE started IS NULL AND scheduled < $t",
        ("$t", StationDb.Iso(now.AddMinutes(-10))));

    public IReadOnlyList<AdPlay> Plays(DateTime from, DateTime to, long? campaignId = null) => db.Query(
        "SELECT id, campaign_id, media_id, advertiser, campaign, spot, break, scheduled, started, length, played FROM ad_plays " +
        "WHERE scheduled >= $f AND scheduled < $t AND ($c IS NULL OR campaign_id = $c) ORDER BY scheduled, id",
        r => new AdPlay(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
            StationDb.ParseIso(r.GetString(7)) ?? default, StationDb.ParseIso(r.GetStringOrNull(8)), r.GetDouble(9), r.IsDBNull(10) ? null : r.GetDouble(10)),
        ("$f", StationDb.Iso(from)), ("$t", StationDb.Iso(to)), ("$c", campaignId));

    /// <summary>Plays today and this hour per campaign, and per spot overall, counting spots that are planned but not yet on air.</summary>
    public AdCounts Counts(AdCampaign c, DateTime at)
    {
        var today = Plays(at.Date, at.Date.AddDays(1), c.Id);
        var perSpot = db.Query("SELECT media_id, COUNT(*) FROM ad_plays WHERE campaign_id = $c GROUP BY media_id",
            r => (r.GetInt64(0), (int)r.GetInt64(1)), ("$c", c.Id)).ToDictionary(x => x.Item1, x => x.Item2);
        return new AdCounts(today.Count, today.Count(p => p.Scheduled.Hour == at.Hour), perSpot);
    }

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Proof of broadcast as CSV (semicolon, for Excel in Dutch).</summary>
    public static string ProofCsv(IEnumerable<AdPlay> plays)
    {
        var sb = new StringBuilder("Adverteerder;Campagne;Spot;Blok;Gepland;Uitgezonden;Lengte (s);Gespeeld (s);Volledig\r\n");
        static string Q(string s) => s.Contains(';') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        foreach (var p in plays)
            sb.Append(CultureInfo.InvariantCulture, $"{Q(p.Advertiser)};{Q(p.Campaign)};{Q(p.Spot)};{Q(p.Break)};{p.Scheduled:yyyy-MM-dd HH:mm:ss};")
              .Append(p.Started is DateTime s ? s.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "")
              .Append(';').Append(p.Length.ToString("0.0", CultureInfo.GetCultureInfo("nl-NL")))
              .Append(';').Append(p.Played?.ToString("0.0", CultureInfo.GetCultureInfo("nl-NL")) ?? "")
              .Append(';').Append(p.Started is null ? "niet uitgezonden" : p.Complete ? "ja" : "nee").Append("\r\n");
        return sb.ToString();
    }
}
