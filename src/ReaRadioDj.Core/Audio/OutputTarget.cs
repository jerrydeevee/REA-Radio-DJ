using System.Globalization;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Where a mixer channel goes. "" = its normal bus (on air, or PFL for the pre-listen channel); "dev:&lt;device&gt;:&lt;pair&gt;"
/// = an output of its own on a Windows sound card, for a physical mixing desk with a fader per player. Pair 0 is
/// output 1/2, pair 1 is 3/4, pair 2 is 5/6 and pair 3 is 7/8 of a multichannel card.
/// </summary>
public readonly record struct OutputTarget(int Device, int Pair)
{
    public const string Default = "";
    public const int Pairs = 4;

    /// <summary>The text form used in settings and by the studio.</summary>
    public string Id => string.Create(CultureInfo.InvariantCulture, $"dev:{Device}:{Pair}");

    public static string Name(int pair) => $"{pair * 2 + 1}/{pair * 2 + 2}";

    public static bool TryParse(string? text, out OutputTarget target)
    {
        target = default;
        if (string.IsNullOrEmpty(text)) return false;
        var p = text.Split(':');
        if (p.Length != 3 || p[0] != "dev") return false;
        if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int device) || device < -1) return false;
        if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pair) || pair is < 0 or >= Pairs) return false;
        target = new OutputTarget(device, pair);
        return true;
    }

    /// <summary>BASS speaker flags for the pair (none for 1/2, so an ordinary stereo card always works).</summary>
    public uint SpeakerFlags => Pair == 0 ? 0u : (uint)(Pair + 1) << 24;
}
