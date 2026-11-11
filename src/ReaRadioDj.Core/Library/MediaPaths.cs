namespace ReaRadioDj.Core.Library;

/// <summary>One replacement location: where a file is missing under <see cref="From"/>, look under <see cref="To"/>.</summary>
public sealed record PathSubstitute(string From, string To, bool Enabled = true);

/// <summary>
/// Replacement locations for audio files. The library keeps the path it read; when that file is not there (the share
/// is down, the drive got another letter, the studio PC has the music elsewhere), the same file is looked up under the
/// replacement folders in order. Used whenever audio is opened or checked.
/// </summary>
public static class MediaPaths
{
    private static volatile IReadOnlyList<PathSubstitute> _rules = [];

    public static IReadOnlyList<PathSubstitute> Rules
    {
        get => _rules;
        set => _rules = Clean(value);
    }

    /// <summary>Drops empty and duplicate rules and gives each folder one trailing separator.</summary>
    public static IReadOnlyList<PathSubstitute> Clean(IEnumerable<PathSubstitute> rules) =>
        [.. rules.Select(r => r with { From = Folder(r.From), To = Folder(r.To) })
                 .Where(r => r.From.Length > 1 && r.To.Length > 1 && !string.Equals(r.From, r.To, StringComparison.OrdinalIgnoreCase))
                 .DistinctBy(r => (r.From.ToUpperInvariant(), r.To.ToUpperInvariant()))];

    private static string Folder(string? path)
    {
        var p = (path ?? "").Trim().Trim('"').Replace('/', '\\');
        return p.Length == 0 ? "" : p.TrimEnd('\\') + "\\";
    }

    /// <summary>The places to look for a file, the stored path first.</summary>
    public static IEnumerable<string> Candidates(string path, IEnumerable<PathSubstitute> rules)
    {
        yield return path;
        if (string.IsNullOrEmpty(path)) yield break;
        var p = path.Replace('/', '\\');
        foreach (var r in rules)
            if (r.Enabled && p.StartsWith(r.From, StringComparison.OrdinalIgnoreCase))
                yield return r.To + p[r.From.Length..];
    }

    /// <summary>The path where the file actually is; the stored path itself when it is there or when no replacement has it.</summary>
    public static string Resolve(string path) => Resolve(path, _rules, File.Exists);

    public static string Resolve(string path, IEnumerable<PathSubstitute> rules, Func<string, bool> exists)
    {
        if (string.IsNullOrEmpty(path)) return path;
        foreach (var c in Candidates(path, rules))
            if (exists(c)) return c;
        return path;
    }

    /// <summary>True when the file is at its path or at a replacement location.</summary>
    public static bool Exists(string path) => !string.IsNullOrEmpty(path) && File.Exists(Resolve(path));
}
