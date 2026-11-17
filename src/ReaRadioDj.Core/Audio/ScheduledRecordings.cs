using System.Globalization;
using ReaRadioDj.Core.Scheduling;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// A recording at a fixed time: every chosen day from <see cref="Start"/> for <see cref="Minutes"/> minutes, of a mixer
/// channel (<c>strip:mic</c>, <c>strip:line1</c> …) or an internet stream (<c>http(s)://…</c>). The result lands in the
/// library (jingles, category Opnamen). With <see cref="Replace"/> every new recording overwrites the previous one under
/// the same name, so the item in the library (and in clocks, the planner and the cart wall) stays the same and always
/// holds the latest edition: the regional news of 12:00, ready to play.
/// </summary>
public sealed record ScheduledRecording(string Id, string Name, bool Enabled, int Days, string Start, int Minutes, string Source,
    bool Replace = true, string Artist = "")
{
    public const int MaxMinutes = 180;

    public TimeSpan? StartTime =>
        TimeSpan.TryParseExact(Start.Trim(), [@"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss"], CultureInfo.InvariantCulture, out var t)
        && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1) ? t : null;

    public TimeSpan Length => TimeSpan.FromMinutes(Math.Clamp(Minutes, 1, MaxMinutes));

    public bool IsStream => Source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mixer channel to record, or null for a stream.</summary>
    public string? StripId => Source.StartsWith("strip:", StringComparison.OrdinalIgnoreCase) ? Source[6..].Trim() : null;

    /// <summary>The start of the edition that is running at <paramref name="now"/> (it may have begun yesterday), or null.</summary>
    public DateTime? Running(DateTime now)
    {
        if (!Enabled || StartTime is not TimeSpan t) return null;
        for (int back = 0; back <= 1; back++)
        {
            var start = now.Date.AddDays(-back) + t;
            if ((Days & TimedEvent.DayBit(start.DayOfWeek)) != 0 && start <= now && now < start + Length) return start;
        }
        return null;
    }

    /// <summary>The next start after <paramref name="now"/> within a week, or null when it never runs.</summary>
    public DateTime? Next(DateTime now)
    {
        if (!Enabled || StartTime is not TimeSpan t || (Days & TimedEvent.AllDays) == 0) return null;
        for (int d = 0; d <= 7; d++)
        {
            var start = now.Date.AddDays(d) + t;
            if (start > now && (Days & TimedEvent.DayBit(start.DayOfWeek)) != 0) return start;
        }
        return null;
    }

    /// <summary>File name in the library: always the same with <see cref="Replace"/>, otherwise with the date in front.</summary>
    public string FileName(DateTime start)
    {
        var safe = string.Concat(Name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "Opname";
        if (safe.Length > 80) safe = safe[..80];
        return Replace ? $"{safe}.mp3" : $"{start:yyyy-MM-dd-HHmm} {safe}.mp3";
    }

    /// <summary>Why this recording cannot run, or null when it is fine.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "geef de opname een naam";
        if (StartTime is null) return "begintijd als 12:00";
        if ((Days & TimedEvent.AllDays) == 0) return "kies minstens één dag";
        if (!IsStream && string.IsNullOrWhiteSpace(StripId)) return "kies een kanaal of geef een streamadres (http://…)";
        return null;
    }

    public ScheduledRecording Checked() => this with
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N")[..10] : Id.Trim(),
        Name = Name.Trim(),
        Days = Days & TimedEvent.AllDays,
        Start = StartTime is TimeSpan t ? t.ToString(t.Seconds == 0 ? @"hh\:mm" : @"hh\:mm\:ss", CultureInfo.InvariantCulture) : Start.Trim(),
        Minutes = Math.Clamp(Minutes, 1, MaxMinutes),
        Source = Source.Trim(),
        Artist = Artist.Trim(),
    };
}
