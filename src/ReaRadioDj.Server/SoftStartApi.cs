using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Server;

/// <summary>How long a soft fixed-time event (news, ads, an item at a fixed time) waits for the item on air.</summary>
public static class SoftStartApi
{
    public const string Key = "soft-max-wait";
    /// <summary>Seconds; 0 = always wait for the end of the item.</summary>
    public const double Max = 900;

    public sealed record SoftStartDto(double MaxWaitSeconds);

    public static double Load(StationDb db) =>
        double.TryParse(db.GetSetting(Key), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, 0, Max) : 0;

    public static void Map(RouteGroupBuilder api, StationDb db, AutomationService automation)
    {
        automation.SoftMaxWait = TimeSpan.FromSeconds(Load(db));
        api.MapGet("/soft-start", () => new SoftStartDto(automation.SoftMaxWait.TotalSeconds));
        api.MapPut("/soft-start", (SoftStartDto d) =>
        {
            double v = double.IsFinite(d.MaxWaitSeconds) ? Math.Clamp(d.MaxWaitSeconds, 0, Max) : 0;
            db.SetSetting(Key, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            automation.SoftMaxWait = TimeSpan.FromSeconds(v);
            return new SoftStartDto(v);
        });
    }
}
