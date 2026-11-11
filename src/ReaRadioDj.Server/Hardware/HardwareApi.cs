using ReaRadioDj.Contracts;
using ReaRadioDj.Core.Hardware;

namespace ReaRadioDj.Server.Hardware;

/// <summary>Hardware devices, their buttons and tallies, what they reported lately, and the ports on this computer.</summary>
public static class HardwareApi
{
    private static readonly string[] StateNames = ["mic", "onair", "auto", "voip1-ringing", "voip1-connected", "voip2-ringing", "voip2-connected"];

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
            var kind = d.Kind switch { "serial" => HwKind.Serial, "livewire" => HwKind.Livewire, "ember" => HwKind.Ember, "mackie" => HwKind.Mackie, _ => HwKind.Midi };
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
        g.MapPost("/devices/{id:long}/simulate", (long id, string trigger, int value, HardwareService service) =>
            service.Simulate(id, trigger, value) ? Results.Ok(service.Recent.FirstOrDefault()) : Results.BadRequest(new { error = "bijv. ch1 note 11 of ch1 cc 7" }));

        // ---- Mackie Control surfaces on the mixer
        g.MapGet("/mackie", (MackieService s, HardwareStore store, HardwareService service) => Mackie(s, store, service));
        g.MapPut("/mackie", (MackieSettingsDto d, MackieService s, HardwareStore store, HardwareService service) =>
        {
            s.SetSettings(new MackieSettings { MasterStrip = d.MasterStrip ?? "", Transport = d.Transport, Meters = d.Meters });
            return Mackie(s, store, service);
        });
        g.MapPost("/mackie/simulate", (MackieSimulateRequest r, MackieService s, HardwareStore store, HardwareService service) =>
            store.Devices().All(d => d.Id != r.DeviceId || d.Kind != HwKind.Mackie) ? Results.BadRequest(new { error = "kies een apparaat van de soort Mackie Control" })
            : s.Simulate(r.DeviceId, r.Messages ?? "") is null ? Results.BadRequest(new { error = "hexadecimale bytes, bijv. 90 10 7F (meerdere met komma's)" })
            : Results.Ok(Mackie(s, store, service)));

        // ---- DJ controller: what each control does, learning, presets
        var m = api.MapGroup("/dj/midi");
        m.MapGet("", (DjMidiService s, HardwareStore store) => Midi(s, store));
        m.MapPost("/learn", (DjMidiLearnRequest r, DjMidiService s, HardwareStore store) =>
            s.Learn(r.Control) ? Results.Ok(Midi(s, store)) : Results.BadRequest(new { error = "onbekende functie" }));
        m.MapPost("/invert", (DjMidiLearnRequest r, DjMidiService s, HardwareStore store) =>
            r.Control is { } c && s.SetInvert(c, r.Invert ?? false) ? Results.Ok(Midi(s, store)) : Results.NotFound());
        m.MapDelete("/bindings/{control}", (string control, DjMidiService s, HardwareStore store) => { s.Remove(control); return Results.Ok(Midi(s, store)); });
        m.MapDelete("/bindings", (DjMidiService s, HardwareStore store) => { s.Clear(); return Results.Ok(Midi(s, store)); });
        m.MapPost("/presets", (DjMidiPresetRequest r, DjMidiService s, HardwareStore store) =>
        {
            if (r.Bindings is { Count: > 0 } list)
                return s.Import(new DjMidiPreset(r.Name ?? "", list.Select(b => new DjMidiBinding(b.Control, 0, b.Trigger, b.Invert)).ToList())) is null
                    ? Results.BadRequest(new { error = "geen bruikbare koppelingen in deze voorinstelling" })
                    : Results.Ok(Midi(s, store));
            return s.SavePreset(r.Name ?? "") ? Results.Ok(Midi(s, store)) : Results.BadRequest(new { error = "geef de voorinstelling een naam" });
        });
        m.MapGet("/presets/{name}", (string name, DjMidiService s) =>
            s.Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { } p
                ? Results.Ok(new DjMidiPresetRequest(p.Name, p.Bindings.Select(Binding).ToList()))
                : Results.NotFound());
        m.MapPost("/presets/{name}/apply", (string name, long deviceId, DjMidiService s, HardwareStore store) =>
            store.Devices().All(d => d.Id != deviceId) ? Results.BadRequest(new { error = "kies de controller" })
            : s.ApplyPreset(name, deviceId) ? Results.Ok(Midi(s, store)) : Results.NotFound());
        m.MapDelete("/presets/{name}", (string name, DjMidiService s, HardwareStore store) => { s.DeletePreset(name); return Results.Ok(Midi(s, store)); });

        g.MapPost("/outputs/{id:long}/test", (long id, bool on, HardwareService service) =>
            service.Test(id, on) ? Results.Ok() : Results.Conflict(new { error = "apparaat niet verbonden" }));
    }

    private static MackieDto Mackie(MackieService s, HardwareStore store, HardwareService service) => new(
        new MackieSettingsDto(s.Settings.MasterStrip, s.Settings.Transport, s.Settings.Meters),
        s.Channels.Select(c => new MackieChannelDto(c.Id, c.Name)).ToList(),
        store.Devices().Where(d => d.Kind == HwKind.Mackie).Select(d =>
        {
            var (connected, status) = service.StatusOf(d.Id);
            return new MackieDeviceDto(d.Id, d.Name, d.Port, connected, d.Enabled ? status : "uit", s.BankOf(d.Id));
        }).ToList(),
        s.Recent);

    private static DjMidiBindingDto Binding(DjMidiBinding b) => new(b.Control, b.DeviceId, b.Trigger, b.Invert);

    private static DjMidiDto Midi(DjMidiService s, HardwareStore store)
    {
        var devices = store.Devices().Where(d => d.Kind == HwKind.Midi).ToList();
        return new DjMidiDto(
            DjMidi.Controls.Select(c => new DjMidiControlDto(c.Id, c.Name, c.Group, c.Kind.ToString().ToLowerInvariant())).ToList(),
            s.Bindings.Select(Binding).ToList(),
            s.Presets.Select(p => new DjMidiPresetInfoDto(p.Name, p.Bindings.Count)).ToList(),
            devices.Select(d => new DjMidiDeviceDto(d.Id, d.Name, d.Port, d.Enabled)).ToList(),
            s.Learning, s.LearnResult, s.LastSignal);
    }

    private static HwDeviceDto Dto(HwDevice d, HardwareService service)
    {
        var (connected, status) = service.StatusOf(d.Id);
        return new HwDeviceDto(d.Id, d.Name, d.Enabled, d.Kind switch { HwKind.Serial => "serial", HwKind.Livewire => "livewire", HwKind.Ember => "ember", HwKind.Mackie => "mackie", _ => "midi" },
            d.Port, d.Baud, d.Password, connected, d.Enabled ? status : "uit");
    }
}
