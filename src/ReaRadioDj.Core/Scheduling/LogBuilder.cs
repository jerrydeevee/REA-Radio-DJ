using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Scheduling;

/// <summary>
/// Simple music rotation ("clock"): random songs with artist and song separation, a jingle every N songs
/// and an AI break every M songs. Keeps the log filled to <see cref="RotationOptions.LookAhead"/> items.
/// </summary>
public sealed class LogBuilder(RotationOptions rotation, bool presenterEnabled, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    public int Fill(PlayoutLog log, IReadOnlyList<MediaItem> songs, IReadOnlyList<MediaItem> jingles)
    {
        int added = 0;
        while (log.Count < rotation.LookAhead && songs.Count > 0 && added < 100)
        {
            var last = log.LastPlanned();
            bool lastWasSong = last?.Type == LogItemType.Song;

            if (lastWasSong && presenterEnabled && rotation.BreakEverySongs > 0
                && log.SongsSinceLast(LogItemType.AiBreak) >= rotation.BreakEverySongs)
            {
                log.Append(new LogItem { Type = LogItemType.AiBreak });
            }
            else if (lastWasSong && jingles.Count > 0 && rotation.JingleEverySongs > 0
                && log.SongsSinceLast(LogItemType.Jingle) >= rotation.JingleEverySongs)
            {
                log.Append(new LogItem { Type = LogItemType.Jingle, Media = jingles[_random.Next(jingles.Count)] });
            }
            else
            {
                log.Append(new LogItem { Type = LogItemType.Song, Media = PickSong(songs, log.RecentAndPlannedSongs()) });
            }
            added++;
        }
        return added;
    }

    internal MediaItem PickSong(IReadOnlyList<MediaItem> songs, IReadOnlyList<MediaItem> recent)
    {
        // Relax the rules step by step for small libraries: first both rules, then only song separation, then anything.
        var recentPaths = recent.TakeLast(rotation.SongSeparation).Select(s => s.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recentArtists = recent.TakeLast(rotation.ArtistSeparation).Select(s => s.Artist)
            .Where(a => !string.IsNullOrWhiteSpace(a)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = songs.Where(s => !recentPaths.Contains(s.Path) && !recentArtists.Contains(s.Artist)).ToList();
        if (candidates.Count == 0) candidates = songs.Where(s => !recentPaths.Contains(s.Path)).ToList();
        if (candidates.Count == 0)
        {
            var lastPath = recent.LastOrDefault()?.Path;
            candidates = songs.Where(s => !string.Equals(s.Path, lastPath, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (candidates.Count == 0) candidates = songs.ToList();
        return candidates[_random.Next(candidates.Count)];
    }
}
