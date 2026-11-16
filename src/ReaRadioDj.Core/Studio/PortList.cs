using System.Globalization;
using System.Text;

namespace ReaRadioDj.Core.Studio;

/// <summary>One port the engine listens on or connects to.</summary>
/// <param name="Protocol">"tcp" or "udp".</param>
/// <param name="Incoming">True for a port others connect to (needs a firewall rule when it is open to the network).</param>
/// <param name="Network">True when it listens on the network, false when only on this computer.</param>
/// <param name="Active">False when it is set up but switched off.</param>
public sealed record PortUse(int Port, string Protocol, bool Incoming, string What, bool Network, bool Active, string Note = "");

/// <summary>
/// The overview of ports the engine uses, for the person who sets up the firewall or the network. Incoming ports that
/// are open to the network are the ones that need a rule; the engine never opens anything in the firewall itself.
/// </summary>
public static class PortList
{
    /// <summary>True for an address that is reachable from this computer only.</summary>
    public static bool IsLocal(string host)
    {
        host = host.Trim().Trim('[', ']');
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "::1" || host.StartsWith("127.", StringComparison.Ordinal);
    }

    /// <summary>The web ports from the engine's addresses (e.g. "http://localhost:8600", "http://0.0.0.0:8600", "http://+:80").</summary>
    public static IEnumerable<PortUse> Web(IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            // "+" and "*" are not valid hosts for Uri, but are what Kestrel uses for "all addresses"
            var fixedUrl = url.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0");
            if (!Uri.TryCreate(fixedUrl, UriKind.Absolute, out var u)) continue;
            yield return new PortUse(u.Port, "tcp", true, $"Web en studio ({u.Scheme})", !IsLocal(u.Host), true,
                "de studio, de pagina op afstand en de API");
        }
    }

    /// <summary>The control ports (TCP, OSC, UDP text); port 0 is off.</summary>
    public static IEnumerable<PortUse> Control(ControlOptions o)
    {
        bool network = !IsLocal(o.BindAddress);
        yield return new PortUse(o.TcpPort, "tcp", true, "Bediening TCP (commando per regel, rea-cmd)", network, o.TcpPort > 0);
        yield return new PortUse(o.OscPort, "udp", true, "Bediening OSC", network, o.OscPort > 0);
        yield return new PortUse(o.UdpPort, "udp", true, "Bediening UDP (tekstcommando's)", network, o.UdpPort > 0);
    }

    /// <summary>Host and port of an Icecast/SHOUTcast address such as "server:8000/live" or "http://server:8000/live".</summary>
    public static (string Host, int Port)? HostPort(string? address, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var a = address.Trim();
        if (!a.Contains("://", StringComparison.Ordinal)) a = "http://" + a;
        if (!Uri.TryCreate(a, UriKind.Absolute, out var u) || u.Host.Length == 0) return null;
        return (u.Host, u.IsDefaultPort && !address.Contains(':', StringComparison.Ordinal) ? defaultPort : u.Port);
    }

    /// <summary>
    /// A PowerShell command that opens the incoming network ports in the Windows firewall for this program only
    /// (to copy and run as administrator; the engine does not run it).
    /// </summary>
    public static string FirewallCommand(IEnumerable<PortUse> ports, string program)
    {
        var sb = new StringBuilder();
        foreach (var g in ports.Where(p => p.Incoming && p.Network && p.Active && p.Port > 0)
                     .GroupBy(p => p.Protocol).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var list = string.Join(",", g.Select(p => p.Port).Distinct().Order().Select(p => p.ToString(CultureInfo.InvariantCulture)));
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(CultureInfo.InvariantCulture,
                $"New-NetFirewallRule -DisplayName \"REA-Radio-DJ {g.Key.ToUpperInvariant()}\" -Direction Inbound -Protocol {g.Key.ToUpperInvariant()} -LocalPort {list} -Program \"{program}\" -Action Allow -Profile Private,Domain");
        }
        return sb.ToString();
    }
}
