using ReaRadioDj.Core.Model;

namespace ReaRadioDj.Core.Library;

/// <summary>
/// Answers "may this be scheduled now, and if not, why?" for the library, with the same separation rules the
/// automatic song choice uses (<see cref="Scheduling.SongPicker"/>): the last songs played plus the songs already planned.
/// </summary>
public sealed class RotationCheck
{
    private readonly Dictionary<string, int> _songAgo = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _artistAgo = new(StringComparer.OrdinalIgnoreCase);
    private readonly RotationOptions _rotation;
    private readonly DateTime _now;

    /// <param name="recentAndPlanned">Songs played recently plus the songs planned, newest last.</param>
    public RotationCheck(IReadOnlyList<MediaItem> recentAndPlanned, RotationOptions rotation, DateTime now)
    {
        _rotation = rotation;
        _now = now;
        for (int i = recentAndPlanned.Count - 1, ago = 1; i >= 0; i--, ago++)
        {
            var m = recentAndPlanned[i];
            if (ago <= rotation.SongSeparation) _songAgo.TryAdd(m.Path, ago);
            if (ago <= rotation.ArtistSeparation && !string.IsNullOrWhiteSpace(m.Artist)) _artistAgo.TryAdd(m.Artist.Trim(), ago);
        }
    }

    /// <summary>Null when the item may be scheduled now; otherwise a short reason in Dutch.</summary>
    public string? Reason(MediaDetails d)
    {
        if (LibrarySearch.Unplayable(d, _now) is { } hard) return hard;
        if (d.Media.Kind != MediaKind.Song) return null;
        if (_songAgo.ContainsKey(d.Media.Path))
            return $"zit in de laatste {_rotation.SongSeparation} nummers" + When(d);
        if (!string.IsNullOrWhiteSpace(d.Media.Artist) && _artistAgo.ContainsKey(d.Media.Artist.Trim()))
            return $"artiest zit in de laatste {_rotation.ArtistSeparation} nummers";
        return null;
    }

    private string When(MediaDetails d) =>
        d.LastPlayed is DateTime t && t.Date == _now.Date ? $" (gespeeld om {t:HH:mm})" : " (gepland)";
}
