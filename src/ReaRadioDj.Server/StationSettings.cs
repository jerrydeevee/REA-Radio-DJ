using System.Text.Json;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>Sound card and microphone settings changed in the studio; stored in the database and applied at start.</summary>
public sealed record RoutingSettings(int OutputDevice, int PreviewDevice, bool MicEnabled, int MicDevice, double MicDuckLevel,
    double OverlayDuckLevel)
{
    public const string Key = "routing";

    public static RoutingSettings From(ReaRadioDjOptions o) => new(o.OutputDevice, o.Routing.PreviewDevice, o.Routing.MicEnabled,
        o.Routing.MicDevice, o.Routing.MicDuckLevel, o.Routing.OverlayDuckLevel);

    public void ApplyTo(ReaRadioDjOptions o)
    {
        o.OutputDevice = OutputDevice;
        o.Routing.PreviewDevice = PreviewDevice;
        o.Routing.MicEnabled = MicEnabled;
        o.Routing.MicDevice = MicDevice;
        o.Routing.MicDuckLevel = Math.Clamp(MicDuckLevel, 0, 1);
        o.Routing.OverlayDuckLevel = Math.Clamp(OverlayDuckLevel, 0, 1);
    }

    /// <summary>Loads stored settings into the options (settings made in the studio win over appsettings.json).</summary>
    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        var json = db.GetSetting(Key);
        if (json is null) return;
        try { JsonSerializer.Deserialize<RoutingSettings>(json, JsonSerializerOptions.Web)?.ApplyTo(o); }
        catch (JsonException) { }
    }

    public void Save(StationDb db) => db.SetSetting(Key, JsonSerializer.Serialize(this, JsonSerializerOptions.Web));
}
