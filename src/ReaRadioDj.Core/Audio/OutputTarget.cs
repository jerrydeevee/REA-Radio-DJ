using System.Globalization;

namespace ReaRadioDj.Core.Audio;

/// <summary>
/// Where audio goes. "" = the normal bus (on air, or PFL for the pre-listen channel); "dev:&lt;device&gt;:&lt;pair&gt;" = an
/// output pair of a Windows sound card; "asio:&lt;device&gt;:&lt;pair&gt;" = an output pair of an ASIO card. Pair 0 is output
/// 1/2, pair 1 is 3/4 and so on.
/// </summary>
public readonly record struct OutputTarget(int Device, int Pair, bool Asio = false)
{
    public const string Default = "";
    /// <summary>Pairs offered for a Windows card (multichannel cards up to 7.1).</summary>
    public const int Pairs = 4;
    /// <summary>Pairs offered for an ASIO card (up to 16 outputs; the card says which exist).</summary>
    public const int AsioPairs = 8;

    /// <summary>The text form used in settings and by the studio.</summary>
    public string Id => string.Create(CultureInfo.InvariantCulture, $"{(Asio ? "asio" : "dev")}:{Device}:{Pair}");

    public static string Name(int pair) => $"{pair * 2 + 1}/{pair * 2 + 2}";

    public static bool TryParse(string? text, out OutputTarget target)
    {
        target = default;
        if (string.IsNullOrEmpty(text)) return false;
        var p = text.Split(':');
        if (p.Length != 3 || p[0] is not ("dev" or "asio")) return false;
        bool asio = p[0] == "asio";
        if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int device) || device < (asio ? 0 : -1)) return false;
        if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pair) || pair < 0 || pair >= (asio ? AsioPairs : Pairs)) return false;
        target = new OutputTarget(device, pair, asio);
        return true;
    }

    /// <summary>BASS speaker flags for a Windows pair (none for 1/2, so an ordinary stereo card always works).</summary>
    public uint SpeakerFlags => Asio || Pair == 0 ? 0u : (uint)(Pair + 1) << 24;
}
