using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Server.Hardware;

/// <summary>Hardware devices, their buttons and tallies, what they reported lately, and the ports on this computer.</summary>
public static class HardwareApi
{
    private static readonly string[] StateNames = ["mic", "onair", "auto"];

    public static void Map(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/hardware");
        g.MapGet("", (HardwareStore store, HardwareService service) => new HwOverviewDto(
            store.Devices().Select(d => Dto(d, service)).ToList(),
            store.Inputs().Select(i => new HwInputDto(i.Id, i.DeviceId, i.Trigger, i.OnCommand, i.OffCommand)).ToList(),
            store.Outputs().Select(o => new HwOutputDto(o.Id, o.DeviceId, StateNames[(int)o.State], o.Target, o.OnText, o.OffText)).ToList(),
            service.States().ToDictionary(s => StateNames[(int)s.Key], s => s.Value)));
        g.MapGet("/ports", () => HardwareService.Ports());
        g.MapGet("/events", (HardwareService service) => service.Recent);

        g.MapPost("/devices", (HwDeviceDto d, HardwareStore store, HardwareService service) =>
        {
            var kind = d.Kind switch { "serial" => HwKind.Serial, "livewire" => HwKind.Livewire, "ember" => HwKind.Ember, _ => HwKind.Midi };
            long id = store.Save(new HwDevice(d.Id, d.Name, d.Enabled, kind, d.Port, d.Baud, d.Password ?? ""));
            return Results.Ok(Dto(store.Devices().First(x => x.Id == id), service));
        });
        g.MapGet("/devices/{id:long}/ember", async (long id, string? path, HardwareService service, CancellationToken ct) =>
            await service.BrowseEmberAsync(id, path?.Trim() ?? "", ct) is { } items
                ? Results.Ok(items.Select(e => new HwEmberItemDto(e.Path, e.Identifier ?? "", e.Description ?? "", !e.IsParameter,
                    e.ValueText, e.IsParameter ? e.EffectiveType.ToString().ToLowerInvariant() : e.Kind, e.Writable)).ToList())
                : Results.Conflict(new { error = "apparaat niet verbonden" }));
        g.MapDelete("/devices/{id:long}", (long id, HardwareStore store) => store.DeleteDevice(id) ? Results.Ok() : Results.NotFound());

        g.MapPost("/inputs", (HwInputDto d, HardwareStore store) =>
        {
            if (store.Devices().All(x => x.Id != d.DeviceId)) return Results.BadRequest(new { error = "kies een apparaat" });
            if (string.IsNullOrWhiteSpace(d.Trigger)) return Results.BadRequest(new { error = "vul in wat het apparaat stuurt" });
            long id = store.Save(new HwInput(d.Id, d.DeviceId, d.Trigger, d.OnCommand ?? "", d.OffCommand ?? ""));
            return Results.Ok(d with { Id = id });
        });
        g.MapDelete("/inputs/{id:long}", (long id, HardwareStore store) => store.DeleteInput(id) ? Results.Ok() : Results.NotFound());

        g.MapPost("/outputs", (HwOutputDto d, HardwareStore store) =>
        {
            if (store.Devices().All(x => x.Id != d.DeviceId)) return Results.BadRequest(new { error = "kies een apparaat" });
            int state = Math.Max(0, Array.IndexOf(StateNames, d.State));
            long id = store.Save(new HwOutput(d.Id, d.DeviceId, (HwState)state, d.Target ?? "", d.OnText ?? "", d.OffText ?? ""));
            return Results.Ok(d with { Id = id });
        });
        g.MapDelete("/outputs/{id:long}", (long id, HardwareStore store) => store.DeleteOutput(id) ? Results.Ok() : Results.NotFound());
        g.MapPost("/outputs/{id:long}/test", (long id, bool on, HardwareService service) =>
            service.Test(id, on) ? Results.Ok() : Results.Conflict(new { error = "apparaat niet verbonden" }));
    }

    private static HwDeviceDto Dto(HwDevice d, HardwareService service)
    {
        var (connected, status) = service.StatusOf(d.Id);
        return new HwDeviceDto(d.Id, d.Name, d.Enabled, d.Kind switch { HwKind.Serial => "serial", HwKind.Livewire => "livewire", HwKind.Ember => "ember", _ => "midi" },
            d.Port, d.Baud, d.Password, connected, d.Enabled ? status : "uit");
    }
}
