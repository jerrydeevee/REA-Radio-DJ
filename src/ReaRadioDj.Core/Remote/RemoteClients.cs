using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ReaRadioDj.Core.Remote;

/// <summary>
/// A remote control with its own limits: a tablet, a button box, a Stream Deck or another program. It is recognised by
/// its own key (sent like the control password: AUTH on TCP, the first word on UDP, ?key= or X-Rea-Key on HTTP) or,
/// without a key, by its address. It may only run the command groups in <see cref="Groups"/>; "Info" (STATUS, HELP)
/// is always allowed. The key itself is never stored, only its hash.
/// </summary>
/// <param name="Addresses">Comma-separated IP addresses or networks (192.168.1.20, 192.168.1.0/24); empty = any address.</param>
public sealed record RemoteClient(string Id, string Name, bool Enabled, string KeyHash, string Addresses, IReadOnlyList<string> Groups)
{
    public bool HasKey => KeyHash.Length > 0;
}

public static class RemoteClients
{
    /// <summary>The group that is always allowed (STATUS, HELP).</summary>
    public const string InfoGroup = "Info";

    /// <summary>A new key: 20 letters and digits, without characters that look alike.</summary>
    public static string NewKey()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
        return string.Create(20, 0, (span, _) =>
        {
            for (int i = 0; i < span.Length; i++) span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }

    public static string Hash(string key) =>
        string.IsNullOrEmpty(key) ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("rea-remote:" + key))).ToLowerInvariant();

    /// <summary>The enabled client with this key, when the address fits too; null for an unknown key.</summary>
    public static RemoteClient? ByKey(IEnumerable<RemoteClient> clients, string? key, IPAddress? from)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var hash = Encoding.ASCII.GetBytes(Hash(key));
        return clients.FirstOrDefault(c => c.Enabled && c.HasKey
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(c.KeyHash), hash) && AddressMatches(c, from));
    }

    /// <summary>The enabled client without a key that is known by this address (the first that matches).</summary>
    public static RemoteClient? ByAddress(IEnumerable<RemoteClient> clients, IPAddress? from) =>
        from is null ? null : clients.FirstOrDefault(c => c.Enabled && !c.HasKey && c.Addresses.Trim().Length > 0 && AddressMatches(c, from));

    public static bool AddressMatches(RemoteClient c, IPAddress? from)
    {
        var list = c.Addresses.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (list.Length == 0) return true;
        if (from is null) return false;
        if (from.IsIPv4MappedToIPv6) from = from.MapToIPv4();
        return list.Any(a => Matches(a, from));
    }

    private static bool Matches(string entry, IPAddress from)
    {
        int slash = entry.IndexOf('/');
        if (slash < 0) return IPAddress.TryParse(entry, out var ip) && (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).Equals(from);
        if (!IPAddress.TryParse(entry[..slash], out var network) || !int.TryParse(entry[(slash + 1)..], out int bits)) return false;
        if (network.AddressFamily != from.AddressFamily) return false;
        var a = network.GetAddressBytes();
        var b = from.GetAddressBytes();
        if (bits < 0 || bits > a.Length * 8) return false;
        for (int i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            int mask = bits >= 8 ? 0xFF : 0xFF << (8 - bits) & 0xFF;
            if ((a[i] & mask) != (b[i] & mask)) return false;
        }
        return true;
    }

    /// <summary>True when the client may run a command of this group (null client = no limits).</summary>
    public static bool Allows(RemoteClient? client, string? group) =>
        client is null || group == InfoGroup || group is not null && client.Groups.Contains(group, StringComparer.OrdinalIgnoreCase);

    /// <summary>Checks a stored list: names, ids and addresses cleaned, unknown groups dropped.</summary>
    public static List<RemoteClient> Clean(IEnumerable<RemoteClient> clients, IReadOnlyCollection<string> groups)
    {
        var result = new List<RemoteClient>();
        foreach (var c in clients)
        {
            if (c is null) continue;
            var addresses = string.Join(", ", (c.Addresses ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Where(a => IPAddress.TryParse(a.Split('/')[0], out _)));
            result.Add(c with
            {
                Id = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("N")[..12] : c.Id.Trim(),
                Name = string.IsNullOrWhiteSpace(c.Name) ? "Afstandsbediening" : c.Name.Trim(),
                KeyHash = c.KeyHash ?? "",
                Addresses = addresses,
                Groups = (c.Groups ?? []).Where(g => groups.Contains(g, StringComparer.OrdinalIgnoreCase) && g != InfoGroup)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            });
        }
        return result;
    }

    /// <summary>The address of an endpoint, as IPv4 when it is an IPv4 address in IPv6 form.</summary>
    public static IPAddress? Of(EndPoint? endPoint) =>
        endPoint is IPEndPoint ip ? (ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4() : ip.Address) : null;

    /// <summary>True for an address on this computer.</summary>
    public static bool IsLocal(IPAddress? ip) => ip is null || IPAddress.IsLoopback(ip) || ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.Equals(IPAddress.IPv6Loopback);
}
