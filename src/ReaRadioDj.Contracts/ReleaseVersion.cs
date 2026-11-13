using System.Globalization;
using System.Text.Json;

namespace ReaRadioDj.Contracts;

/// <summary>
/// A program version such as "1.1.0", "v1.0.1" or "1.1.0-rc.1+111c77d".
/// The build part after "+" is ignored. A test version ("-rc.1") comes before the release with the same numbers.
/// </summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => Prerelease.Length > 0;

    public static ReleaseVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var pre = "";
        var dash = s.IndexOf('-');
        if (dash >= 0) { pre = s[(dash + 1)..]; s = s[..dash]; }
        var parts = s.Split('.');
        if (parts.Length is < 1 or > 4) return null;
        var n = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return null;
            if (i < 3) n[i] = v;
        }
        return new ReleaseVersion(n[0], n[1], n[2], pre);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>"rc.2" after "rc.1", "rc.10" after "rc.9": number parts compare as numbers.</summary>
    private static int ComparePrerelease(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var na = int.TryParse(pa[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ia);
            var nb = int.TryParse(pb[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ib);
            var c = na && nb ? ia.CompareTo(ib)
                : na ? -1 : nb ? 1
                : string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + Prerelease : "");
}

/// <summary>The newest published release, as GitHub reports it (releases/latest).</summary>
public sealed record ReleaseInfo(ReleaseVersion Version, string Tag, string Url, string Name)
{
    /// <summary>Reads tag_name, html_url and name from the GitHub answer; null when the answer is not usable.</summary>
    public static ReleaseInfo? Parse(string json, string fallbackUrl)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            var version = ReleaseVersion.Parse(tag);
            if (version is null) return null;
            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
            // Only a link to the project's own GitHub pages is opened.
            if (url is null || !url.StartsWith(fallbackUrl, StringComparison.OrdinalIgnoreCase)) url = fallbackUrl + "/releases";
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            return new ReleaseInfo(version, tag!, url, string.IsNullOrWhiteSpace(name) ? tag! : name!);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>True when this release is newer than the running version.</summary>
    public bool IsNewerThan(string? current) =>
        ReleaseVersion.Parse(current) is { } cur && Version > cur;
}
