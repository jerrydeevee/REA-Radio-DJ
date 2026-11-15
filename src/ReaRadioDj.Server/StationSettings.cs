using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Audio;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>
/// Sound card and microphone settings changed in the studio; stored in the database and applied at start. The cards are
/// stored with their names: at start the name decides which number they have now (see <see cref="DeviceChoice"/>).
/// </summary>
public sealed record RoutingSettings(int OutputDevice, int PreviewDevice, bool MicEnabled, int MicDevice, double MicDuckLevel,
    double OverlayDuckLevel, int MicChannel = 0, string? OutputName = null, string? PreviewName = null, string? MicName = null)
{
    public const string Key = "routing";

    public static RoutingSettings From(ReaRadioDjOptions o) => new(o.OutputDevice, o.Routing.PreviewDevice, o.Routing.MicEnabled,
        o.Routing.MicDevice, o.Routing.MicDuckLevel, o.Routing.OverlayDuckLevel, o.Routing.MicChannel);

    public void ApplyTo(ReaRadioDjOptions o)
    {
        o.OutputDevice = OutputDevice;
        o.Routing.PreviewDevice = PreviewDevice;
        o.Routing.MicEnabled = MicEnabled;
        o.Routing.MicDevice = MicDevice;
        o.Routing.MicChannel = Core.Audio.MicChannel.Checked(MicChannel);   // Core.Audio: the record has a member MicChannel
        o.Routing.MicDuckLevel = Math.Clamp(MicDuckLevel, 0, 1);
        o.Routing.OverlayDuckLevel = Math.Clamp(OverlayDuckLevel, 0, 1);
    }

    /// <summary>Loads stored settings into the options (settings made in the studio win over appsettings.json).</summary>
    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        var json = db.GetSetting(Key);
        if (json is null) return;
        RoutingSettings? s;
        try { s = JsonSerializer.Deserialize<RoutingSettings>(json, JsonSerializerOptions.Web); }
        catch (JsonException) { return; }
        if (s is null) return;
        IReadOnlyList<AudioDevice> outs = [], ins = [];
        try { outs = BassRuntime.GetDevices(); ins = BassRuntime.GetRecordDevices(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        s.Resolved(outs, ins, o.Routing.DeviceNotes).ApplyTo(o);
    }

    /// <summary>The settings with the numbers the stored cards have now (by name); what was not found goes in notes.</summary>
    public RoutingSettings Resolved(IReadOnlyList<AudioDevice> outputs, IReadOnlyList<AudioDevice> inputs, List<string> notes)
    {
        var (output, n1) = DeviceChoice.Resolve(OutputDevice, OutputName, outputs, "Uitzending");
        var (preview, n2) = DeviceChoice.Resolve(PreviewDevice, PreviewName, outputs, "PFL");
        var (mic, n3) = DeviceChoice.Resolve(MicDevice, MicName, inputs, "Microfoon");
        foreach (var n in new[] { n1, n2, n3 }) if (n is not null) notes.Add(n);
        return this with { OutputDevice = output, PreviewDevice = preview, MicDevice = mic };
    }

    /// <summary>Stores the settings together with the names of the chosen cards.</summary>
    public void Save(StationDb db)
    {
        var named = this;
        try
        {
            named = this with
            {
                OutputName = DeviceChoice.NameOf(OutputDevice, BassRuntime.GetDevices()),
                PreviewName = DeviceChoice.NameOf(PreviewDevice, BassRuntime.GetDevices()),
                MicName = DeviceChoice.NameOf(MicDevice, BassRuntime.GetRecordDevices()),
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        db.SetSetting(Key, JsonSerializer.Serialize(named, JsonSerializerOptions.Web));
    }
}
