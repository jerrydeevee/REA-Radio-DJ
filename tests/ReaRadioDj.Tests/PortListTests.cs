using ReaRadioDj.Core;
using ReaRadioDj.Core.Studio;

namespace ReaRadioDj.Tests;

/// <summary>The port overview and the firewall command (pure logic, nothing is opened).</summary>
public class PortListTests
{
    [Fact]
    public void Web_addresses_say_whether_the_network_can_reach_them()
    {
        var ports = PortList.Web(["http://localhost:8600", "http://0.0.0.0:8700", "http://+:8800", "http://127.0.0.1:8612"]).ToList();
        Assert.Equal([8600, 8700, 8800, 8612], ports.Select(p => p.Port));
        Assert.Equal([false, true, true, false], ports.Select(p => p.Network));
        Assert.All(ports, p => Assert.True(p.Incoming));
    }

    [Fact]
    public void Control_ports_follow_the_bind_address_and_zero_is_off()
    {
        var local = PortList.Control(new ControlOptions { BindAddress = "127.0.0.1", TcpPort = 8601, OscPort = 0, UdpPort = 8603 }).ToList();
        Assert.All(local, p => Assert.False(p.Network));
        Assert.False(local.Single(p => p.What.Contains("OSC")).Active);
        var open = PortList.Control(new ControlOptions { BindAddress = "0.0.0.0" }).ToList();
        Assert.All(open, p => Assert.True(p.Network));
    }

    [Fact]
    public void Stream_and_device_addresses_are_read_with_a_default_port()
    {
        Assert.Equal(("localhost", 8000), PortList.HostPort("localhost:8000/live", 8000));
        Assert.Equal(("stream.example.org", 8000), PortList.HostPort("stream.example.org/live", 8000));
        Assert.Equal(("stream.example.org", 80), PortList.HostPort("http://stream.example.org/live", 8000));
        Assert.Equal(("10.0.0.5", 93), PortList.HostPort("10.0.0.5", 93));
        Assert.Null(PortList.HostPort("", 93));
    }

    [Fact]
    public void The_firewall_command_only_opens_incoming_network_ports_that_are_on()
    {
        PortUse[] ports =
        [
            new(8600, "tcp", true, "web", true, true),
            new(8601, "tcp", true, "tcp", false, true),     // this pc only
            new(8602, "udp", true, "osc", true, false),     // off
            new(5060, "udp", true, "sip", true, true),
            new(5004, "udp", true, "rtp", true, true),
            new(8000, "tcp", false, "stream", true, true),  // outgoing
        ];
        var cmd = PortList.FirewallCommand(ports, @"C:\REA\REA-Radio-DJ.exe");
        var lines = cmd.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains("-Protocol TCP -LocalPort 8600 ", lines[0]);
        Assert.Contains("-Protocol UDP -LocalPort 5004,5060 ", lines[1]);
        Assert.Contains("-Program \"C:\\REA\\REA-Radio-DJ.exe\"", lines[0]);
        Assert.Equal("", PortList.FirewallCommand([new(8601, "tcp", true, "tcp", false, true)], "x.exe"));
    }
}
