using System.Globalization;
using System.Security;
using System.Text;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// A programme that is published as "uitzending gemist" / podcast: after each broadcast (on the chosen days from
/// <see cref="Start"/> to <see cref="End"/>) it is cut from the program recording into an episode, the newest
/// <see cref="Keep"/> are kept and an RSS feed is written beside them. Optionally everything is copied to a folder
/// that a web server publishes (<see cref="CopyFolder"/>, with <see cref="PublicUrl"/> as its address).
/// </summary>
public sealed record PodcastProgram(string Id, string Name, bool Enabled, int Days, string Start, string End, string Description = "",
    string Author = "", int Keep = 10, string CopyFolder = "", string PublicUrl = "")
{
    private static readonly string[] TimeFormats = [@"h\:mm", @"hh\:mm"];

    public static TimeSpan? Time(string text) =>
        TimeSpan.TryParseExact(text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    /// <summary>The length of one broadcast; an end before the start runs past midnight.</summary>
    public TimeSpan? Length => Time(Start) is TimeSpan s && Time(End) is TimeSpan e ? (e > s ? e - s : e + TimeSpan.FromDays(1) - s) : null;

    /// <summary>The broadcasts that ended between <paramref name="since"/> and <paramref name="now"/>, oldest first.</summary>
    public IEnumerable<(DateTime From, DateTime To)> EndedBetween(DateTime since, DateTime now)
    {
        if (!Enabled || Time(Start) is not TimeSpan s || Length is not TimeSpan len) yield break;
        for (var day = since.Date.AddDays(-1); day <= now.Date; day = day.AddDays(1))
        {
            var from = day + s;
            var to = from + len;
            if ((Days & TimedEvent.DayBit(from.DayOfWeek)) != 0 && to > since && to <= now) yield return (from, to);
        }
    }

    public string Folder(string root) => Path.Combine(root, Safe(Name));

    public string EpisodeFile(DateTime from) => $"{from:yyyy-MM-dd-HHmm} {Safe(Name)}.mp3";

    public static string Safe(string name)
    {
        var s = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (s.Length == 0) s = "Programma";
        return s.Length > 60 ? s[..60] : s;
    }

    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "geef het programma een naam";
        if (Time(Start) is null || Time(End) is null) return "begin- en eindtijd als 07:00 en 09:00";
        if (Time(Start) == Time(End)) return "het einde moet na het begin liggen";
        if (Length > RecordingCutter.MaxLength) return $"hoogstens {RecordingCutter.MaxLength.TotalHours:0} uur per uitzending";
        if ((Days & TimedEvent.AllDays) == 0) return "kies minstens één dag";
        if (PublicUrl.Trim().Length > 0 && !Uri.TryCreate(PublicUrl.Trim(), UriKind.Absolute, out _)) return "het webadres klopt niet (begin met https://)";
        return null;
    }

    public PodcastProgram Checked() => this with
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N")[..10] : Id.Trim(),
        Name = Name.Trim(),
        Days = Days & TimedEvent.AllDays,
        Start = Time(Start) is TimeSpan s ? s.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : Start.Trim(),
        End = Time(End) is TimeSpan e ? e.ToString(@"hh\:mm", CultureInfo.InvariantCulture) : End.Trim(),
        Description = Description.Trim(),
        Author = Author.Trim(),
        Keep = Math.Clamp(Keep, 1, 500),
        CopyFolder = CopyFolder.Trim(),
        PublicUrl = PublicUrl.Trim(),
    };
}

public sealed record PodcastEpisode(string File, DateTime Aired, double Seconds, long Bytes);

public static class PodcastFeed
{
    /// <summary>
    /// The RSS 2.0 feed (with the iTunes tags podcast apps expect) for the episodes, newest first. Each enclosure is
    /// <paramref name="baseUrl"/> + the file name.
    /// </summary>
    public static string Rss(PodcastProgram p, string station, IEnumerable<PodcastEpisode> episodes, string baseUrl, DateTime now)
    {
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        static string X(string? s) => SecurityElement.Escape(s ?? "") ?? "";
        static string Rfc822(DateTime t) => t.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);
        var description = p.Description.Length > 0 ? p.Description : $"{p.Name} op {station}";
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<rss version=\"2.0\" xmlns:itunes=\"http://www.itunes.com/dtds/podcast-1.0.dtd\">");
        sb.AppendLine("<channel>");
        sb.AppendLine($"  <title>{X(p.Name)}</title>");
        sb.AppendLine($"  <link>{X(baseUrl)}</link>");
        sb.AppendLine($"  <description>{X(description)}</description>");
        sb.AppendLine($"  <itunes:author>{X(p.Author.Length > 0 ? p.Author : station)}</itunes:author>");
        sb.AppendLine($"  <lastBuildDate>{Rfc822(now)}</lastBuildDate>");
        sb.AppendLine("  <generator>REA-Radio-DJ</generator>");
        foreach (var e in episodes.OrderByDescending(e => e.Aired))
        {
            var url = baseUrl + Uri.EscapeDataString(e.File);
            sb.AppendLine("  <item>");
            sb.AppendLine($"    <title>{X($"{p.Name} {e.Aired.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)}")}</title>");
            sb.AppendLine($"    <guid isPermaLink=\"false\">{X(p.Id + "-" + e.Aired.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture))}</guid>");
            sb.AppendLine($"    <pubDate>{Rfc822(e.Aired)}</pubDate>");
            sb.AppendLine($"    <enclosure url=\"{X(url)}\" length=\"{e.Bytes}\" type=\"audio/mpeg\" />");
            sb.AppendLine($"    <itunes:duration>{TimeSpan.FromSeconds(Math.Round(e.Seconds)):h\\:mm\\:ss}</itunes:duration>");
            sb.AppendLine("  </item>");
        }
        sb.AppendLine("</channel>");
        sb.AppendLine("</rss>");
        return sb.ToString();
    }
}
