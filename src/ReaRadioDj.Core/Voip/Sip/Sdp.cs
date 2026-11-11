using System.Text;

namespace ReaRadioDj.Core.Voip.Sip;

/// <summary>The audio part of an SDP offer or answer (RFC 4566 / 3264): where to send RTP and with which codec.</summary>
public sealed record SdpAudio(string Address, int Port, IReadOnlyList<(int PayloadType, string Rtpmap)> Formats, string Direction = "sendrecv")
{
    /// <summary>Our offer or answer for one or more codecs, best first (RFC 3551 static types need no rtpmap, we add it anyway).</summary>
    public static string Build(string address, int port, IEnumerable<IRtpCodec> codecs, long sessionId, int version = 1)
    {
        var list = codecs.ToList();
        var sb = new StringBuilder();
        sb.Append("v=0\r\n");
        sb.Append($"o=REA-Radio-DJ {sessionId} {version} IN IP4 {address}\r\n");
        sb.Append("s=REA-Radio-DJ\r\n");
        sb.Append($"c=IN IP4 {address}\r\n");
        sb.Append("t=0 0\r\n");
        sb.Append($"m=audio {port} RTP/AVP {string.Join(' ', list.Select(c => c.PayloadType))}\r\n");
        foreach (var c in list) sb.Append($"a=rtpmap:{c.PayloadType} {c.Rtpmap}\r\n");
        sb.Append("a=ptime:20\r\n");
        sb.Append("a=sendrecv\r\n");
        return sb.ToString();
    }

    /// <summary>Reads the first audio stream of an SDP body; null when there is none.</summary>
    public static SdpAudio? Parse(string? sdp)
    {
        if (string.IsNullOrWhiteSpace(sdp)) return null;
        string session = "", media = "", direction = "sendrecv";
        int port = -1;
        var pts = new List<int>();
        var maps = new Dictionary<int, string>();
        bool inAudio = false, seenAudio = false;
        foreach (var raw in sdp.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 2 || line[1] != '=') continue;
            char type = line[0];
            string value = line[2..];
            if (type == 'm')
            {
                if (seenAudio) break;
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                inAudio = parts.Length >= 4 && parts[0] == "audio" && parts[2].StartsWith("RTP/AVP", StringComparison.Ordinal);
                if (inAudio)
                {
                    seenAudio = true;
                    port = int.TryParse(parts[1], out var p) ? p : -1;
                    pts.AddRange(parts.Skip(3).Select(x => int.TryParse(x, out var t) ? t : -1).Where(t => t >= 0));
                }
                continue;
            }
            if (type == 'c')
            {
                var parts = value.Split(' ');
                if (parts.Length >= 3) { if (seenAudio && inAudio) media = parts[2].Split('/')[0]; else if (!seenAudio) session = parts[2].Split('/')[0]; }
                continue;
            }
            if (type == 'a' && (inAudio || !seenAudio))
            {
                if (value.StartsWith("rtpmap:", StringComparison.Ordinal))
                {
                    var rest = value[7..].Split(' ', 2);
                    if (rest.Length == 2 && int.TryParse(rest[0], out var pt)) maps[pt] = rest[1].Trim();
                }
                else if (value is "sendrecv" or "sendonly" or "recvonly" or "inactive") direction = value;
            }
        }
        if (!seenAudio || port < 0) return null;
        string address = media.Length > 0 ? media : session;
        var formats = pts.Select(pt => (pt, maps.GetValueOrDefault(pt) ?? StaticMap(pt))).ToList();
        return new SdpAudio(address, port, formats, direction);
    }

    private static string StaticMap(int pt) => pt switch { 0 => "PCMU/8000", 8 => "PCMA/8000", 9 => "G722/8000", 18 => "G729/8000", _ => "" };

    /// <summary>The first codec of <paramref name="preferred"/> (our order) that the other side also offers, with its payload type there.</summary>
    public (IRtpCodec Codec, int PayloadType)? Choose(IEnumerable<string> preferred)
    {
        foreach (var name in preferred)
        {
            IRtpCodec c;
            try { c = RtpCodecs.Create(name); } catch (ArgumentException) { continue; }
            foreach (var (pt, map) in Formats)
                if (map.Equals(c.Rtpmap, StringComparison.OrdinalIgnoreCase) || (map.Length == 0 && pt == c.PayloadType) ||
                    (c.Rtpmap.StartsWith("L16/48000", StringComparison.OrdinalIgnoreCase) && map.Equals("L16/48000", StringComparison.OrdinalIgnoreCase)))
                    return (c, pt);
        }
        return null;
    }
}
