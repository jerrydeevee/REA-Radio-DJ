namespace ReaRadioDj.Core.Voip;

/// <summary>
/// A station in the Verbindingen address book: a SIP call (Kind "sip", Target = number or sip address) or a fixed RTP
/// line (Kind "rtp", with the ports, the far end's address and the codec).
/// </summary>
public sealed record VoipStation(string Id, string Name, string Kind, string Target, int LocalPort, string RemoteHost, int RemotePort,
    string Codec, double JitterMs, string Note);

/// <summary>Checking and finding stations, and filling in the signal commands. Pure functions, so they can be tested.</summary>
public static class VoipStations
{
    public const int Max = 200;

    /// <summary>The checked, tidied list (ids made where missing), or null with an error text.</summary>
    public static IReadOnlyList<VoipStation>? Clean(IEnumerable<VoipStation> list, out string? error, Func<string>? newId = null)
    {
        newId ??= () => Guid.NewGuid().ToString("N")[..10];
        var clean = new List<VoipStation>();
        error = null;
        foreach (var s in list)
        {
            if (clean.Count == Max) { error = $"ten hoogste {Max} stations"; return null; }
            string name = (s.Name ?? "").Trim();
            string kind = (s.Kind ?? "").Trim().ToLowerInvariant();
            string target = (s.Target ?? "").Trim();
            string host = (s.RemoteHost ?? "").Trim();
            string codec = string.IsNullOrWhiteSpace(s.Codec) ? "pcma" : s.Codec.Trim().ToLowerInvariant();
            if (name.Length == 0) { error = "elk station heeft een naam nodig"; return null; }
            if (name.Length > 60) { error = $"naam te lang: {name[..20]}…"; return null; }
            if (kind is not ("sip" or "rtp")) { error = $"{name}: soort is sip of rtp"; return null; }
            if (kind == "sip" && target.Length == 0) { error = $"{name}: vul het nummer of SIP-adres in"; return null; }
            if (kind == "sip" && target.Any(c => char.IsControl(c) || c is '<' or '>' or '"')) { error = $"{name}: het nummer bevat ongeldige tekens"; return null; }
            if (kind == "rtp")
            {
                if (!RtpCodecs.Names.Contains(codec)) { error = $"{name}: onbekende codec {codec}"; return null; }
                if (s.LocalPort is < 1024 or > 65535) { error = $"{name}: eigen poort tussen 1024 en 65535"; return null; }
                if (host.Length > 0 && s.RemotePort is < 1 or > 65535) { error = $"{name}: poort van de overkant ongeldig"; return null; }
                if (host.Any(char.IsWhiteSpace)) { error = $"{name}: het adres bevat spaties"; return null; }
            }
            if (clean.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { error = $"de naam {name} staat er twee keer in"; return null; }
            string id = string.IsNullOrWhiteSpace(s.Id) ? newId() : s.Id.Trim();
            while (clean.Any(c => c.Id == id)) id = newId();
            clean.Add(kind == "sip"
                ? new VoipStation(id, name, kind, target, 0, "", 0, codec, 60, (s.Note ?? "").Trim())
                : new VoipStation(id, name, kind, "", s.LocalPort, host, host.Length > 0 ? s.RemotePort : 0, codec, Math.Clamp(s.JitterMs, 20, 1000), (s.Note ?? "").Trim()));
        }
        return clean;
    }

    /// <summary>The station with this id, else with this name (case-insensitive).</summary>
    public static VoipStation? Find(IEnumerable<VoipStation> list, string idOrName)
    {
        idOrName = (idOrName ?? "").Trim().Trim('"');
        var all = list.ToList();
        return all.FirstOrDefault(s => s.Id == idOrName) ?? all.FirstOrDefault(s => s.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The commands of a signal for one channel: "{kanaal}" becomes voip1/voip2 and "{nr}" 1/2; several commands are
    /// separated by ";".
    /// </summary>
    public static IReadOnlyList<string> Commands(string? signal, string channel)
    {
        if (string.IsNullOrWhiteSpace(signal)) return [];
        string nr = channel.Length > 0 && char.IsDigit(channel[^1]) ? channel[^1..] : channel;
        return signal.Replace("{kanaal}", channel).Replace("{nr}", nr)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>"1", "voip1" → voip1; "2", "voip2" → voip2; anything else null.</summary>
    public static string? Channel(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "1" or "voip1" => "voip1",
        "2" or "voip2" => "voip2",
        _ => null,
    };
}
