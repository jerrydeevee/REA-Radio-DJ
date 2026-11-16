using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Hardware;
using ReaRadioDj.Core.Studio;
using ReaRadioDj.Server.Voip;

namespace ReaRadioDj.Server;

/// <summary>Which ports the engine uses (GET /api/system/ports), with a firewall command for the incoming network ports.</summary>
public static class PortsApi
{
    public static void Map(RouteGroupBuilder api, ReaRadioDjOptions options)
    {
        api.MapGet("/system/ports", (IServer server, VoipService voip, FailoverService failover, StationDb db, HardwareStore hardware) =>
        {
            var urls = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
            var ports = new List<PortUse>();
            ports.AddRange(PortList.Web(urls));
            ports.AddRange(PortList.Control(options.Control));
            try { ports.AddRange(voip.Ports()); } catch (Exception ex) when (ex is not OutOfMemoryException) { }

            var f = failover.Settings;
            if (f.Role != "uit" && PortList.HostPort(f.Partner, 8600) is { } partner)
                ports.Add(new(partner.Port, "tcp", false, $"Reservestudio: de andere engine op {partner.Host}", true, true));
            if (PortList.HostPort(options.Stream.Server, 8000) is { } main)
                ports.Add(new(main.Port, "tcp", false, $"Stream naar {main.Host}", !PortList.IsLocal(main.Host), options.Stream.Enabled));
            foreach (var t in StreamTargetsApi.Load(db))
                if (PortList.HostPort(t.Server, 8000) is { } hp)
                    ports.Add(new(hp.Port, "tcp", false, $"Stream {t.Name} naar {hp.Host}", !PortList.IsLocal(hp.Host), t.Enabled));
            foreach (var d in hardware.Devices().Where(d => d.Kind is HwKind.Livewire or HwKind.Ember))
            {
                int def = d.Kind == HwKind.Livewire ? HwSignals.LivewirePort : Hardware.EmberConnection.DefaultPort;
                if (PortList.HostPort(d.Port, def) is { } hp)
                    ports.Add(new(hp.Port, "tcp", false, $"{(d.Kind == HwKind.Livewire ? "Livewire" : "Ember+")} {d.Name} op {hp.Host}", true, d.Enabled));
            }

            var program = Environment.ProcessPath ?? "REA-Radio-DJ.exe";
            var list = ports
                .OrderByDescending(p => p.Incoming).ThenBy(p => p.Port)
                .Select(p => new PortDto(p.Port, p.Protocol, p.Incoming ? "in" : "uit", p.What,
                    !p.Incoming ? "naar buiten" : p.Network ? "netwerk" : "alleen deze pc", p.Active && p.Port > 0, p.Port == 0 ? "uit" : p.Note))
                .ToList();
            return new PortsDto(list, PortList.FirewallCommand(ports, program), program);
        });
    }
}
