using System.Text.Json;
using ReaRadioDj.Core.Data;

namespace ReaRadioDj.Core;

/// <summary>
/// Everything about the station that used to be set only in appsettings.json: name and folders, mixing, rotation,
/// the stream and remote control. Changed in the studio, stored in the database and applied at start (settings made
/// in the studio win over appsettings.json). Passwords are stored but never sent back to the studio.
/// </summary>
public sealed record GeneralSettings(
    string StationName, string MusicFolder, string JingleFolder, bool StartAutomation, int StandbyPlayers,
    double TargetLufs, double CueInDb, double MixDb, double CueOutDb, double MaxOverlap, double FadeOut, double DuckLevel,
    int ArtistSeparation, int SongSeparation, int LookAhead,
    bool StreamEnabled, string StreamServer, string StreamPassword, int StreamBitrate, string StreamGenre, string StreamUrl,
    string ControlBindAddress, int ControlTcpPort, int ControlOscPort, string ControlPassword,
    bool SilenceEnabled = true, double SilenceDb = -45, double SilenceSeconds = 20, string SilenceAction = "autoplay",
    long? SilenceCategoryId = null)
{
    public const string Key = "general";

    public static GeneralSettings From(ReaRadioDjOptions o) => new(
        o.StationName, o.MusicFolder, o.JingleFolder, o.StartAutomation, o.Routing.StandbyPlayers,
        o.Mix.TargetLufs, o.Mix.CueInDb, o.Mix.MixDb, o.Mix.CueOutDb, o.Mix.MaxOverlap, o.Mix.FadeOut, o.Mix.DuckLevel,
        o.Rotation.ArtistSeparation, o.Rotation.SongSeparation, o.Rotation.LookAhead,
        o.Stream.Enabled, o.Stream.Server, o.Stream.Password, o.Stream.Bitrate, o.Stream.Genre, o.Stream.Url,
        o.Control.BindAddress, o.Control.TcpPort, o.Control.OscPort, o.Control.Password,
        o.Silence.Enabled, o.Silence.ThresholdDb, o.Silence.Seconds, o.Silence.Action, o.Silence.CategoryId);

    /// <summary>
    /// What the studio sent, made safe to use: everything out of range is pulled back, an empty name or folder keeps the
    /// current one, and an empty password keeps the stored one (unless the control password is cleared on purpose).
    /// </summary>
    public GeneralSettings Checked(GeneralSettings current, bool clearControlPassword = false) => this with
    {
        StationName = Text(StationName, current.StationName), MusicFolder = Text(MusicFolder, current.MusicFolder),
        JingleFolder = Text(JingleFolder, current.JingleFolder), StandbyPlayers = Math.Clamp(StandbyPlayers, 1, 4),
        TargetLufs = Math.Clamp(TargetLufs, -30, -8), CueInDb = Math.Clamp(CueInDb, -80, -10), MixDb = Math.Clamp(MixDb, -60, -6),
        CueOutDb = Math.Clamp(CueOutDb, -80, -10), MaxOverlap = Math.Clamp(MaxOverlap, 0, 30), FadeOut = Math.Clamp(FadeOut, 0, 15),
        DuckLevel = Math.Clamp(DuckLevel, 0, 1), ArtistSeparation = Math.Clamp(ArtistSeparation, 0, 100),
        SongSeparation = Math.Clamp(SongSeparation, 0, 1000), LookAhead = Math.Clamp(LookAhead, 2, 40),
        StreamServer = (StreamServer ?? "").Trim(), StreamPassword = string.IsNullOrEmpty(StreamPassword) ? current.StreamPassword : StreamPassword,
        StreamBitrate = Bitrates.Contains(StreamBitrate) ? StreamBitrate : current.StreamBitrate,
        StreamGenre = (StreamGenre ?? "").Trim(), StreamUrl = (StreamUrl ?? "").Trim(),
        ControlBindAddress = Text(ControlBindAddress, current.ControlBindAddress), ControlTcpPort = Port(ControlTcpPort, current.ControlTcpPort),
        ControlOscPort = Port(ControlOscPort, current.ControlOscPort),
        ControlPassword = clearControlPassword ? "" : string.IsNullOrEmpty(ControlPassword) ? current.ControlPassword : ControlPassword,
        SilenceDb = Math.Clamp(SilenceDb, -80, -10), SilenceSeconds = Math.Clamp(SilenceSeconds, 3, 600),
        SilenceAction = SilenceAction is "warn" or "category" ? SilenceAction : "autoplay",
    };

    public static readonly int[] Bitrates = [64, 96, 128, 160, 192, 256, 320];

    private static string Text(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static int Port(int value, int fallback) => value is 0 or (>= 1024 and <= 65535) ? value : fallback;

    /// <summary>Changes the options the engine works with. The mix, rotation and stream objects stay the same instances, so running parts see the new values.</summary>
    public void ApplyTo(ReaRadioDjOptions o)
    {
        o.StationName = StationName;
        o.MusicFolder = MusicFolder;
        o.JingleFolder = JingleFolder;
        o.StartAutomation = StartAutomation;
        o.Routing.StandbyPlayers = StandbyPlayers;
        o.Mix.TargetLufs = TargetLufs; o.Mix.CueInDb = CueInDb; o.Mix.MixDb = MixDb; o.Mix.CueOutDb = CueOutDb;
        o.Mix.MaxOverlap = MaxOverlap; o.Mix.FadeOut = FadeOut; o.Mix.DuckLevel = DuckLevel;
        o.Rotation.ArtistSeparation = ArtistSeparation; o.Rotation.SongSeparation = SongSeparation; o.Rotation.LookAhead = LookAhead;
        o.Stream.Enabled = StreamEnabled; o.Stream.Server = StreamServer; o.Stream.Password = StreamPassword;
        o.Stream.Bitrate = StreamBitrate; o.Stream.Genre = StreamGenre; o.Stream.Url = StreamUrl;
        o.Control.BindAddress = ControlBindAddress; o.Control.TcpPort = ControlTcpPort; o.Control.OscPort = ControlOscPort;
        o.Control.Password = ControlPassword;
        o.Silence.Enabled = SilenceEnabled; o.Silence.ThresholdDb = SilenceDb; o.Silence.Seconds = SilenceSeconds; o.Silence.Action = SilenceAction; o.Silence.CategoryId = SilenceCategoryId;
    }

    /// <summary>What only takes effect after the engine has been started again.</summary>
    public IReadOnlyList<string> NeedsRestart(GeneralSettings old)
    {
        var list = new List<string>();
        if (StandbyPlayers != old.StandbyPlayers) list.Add("het aantal standby-spelers");
        if (ControlBindAddress != old.ControlBindAddress || ControlTcpPort != old.ControlTcpPort || ControlOscPort != old.ControlOscPort)
            list.Add("adres en poorten van de bediening op afstand");
        if (StationName != old.StationName) list.Add("de stationsnaam in de stream");
        return list;
    }

    public bool FoldersChanged(GeneralSettings old) =>
        !string.Equals(MusicFolder, old.MusicFolder, StringComparison.OrdinalIgnoreCase) || !string.Equals(JingleFolder, old.JingleFolder, StringComparison.OrdinalIgnoreCase);

    public static void Load(StationDb db, ReaRadioDjOptions o)
    {
        var json = db.GetSetting(Key);
        if (json is null) return;
        try { JsonSerializer.Deserialize<GeneralSettings>(json, JsonSerializerOptions.Web)?.ApplyTo(o); }
        catch (JsonException) { }
    }

    public void Save(StationDb db) => db.SetSetting(Key, JsonSerializer.Serialize(this, JsonSerializerOptions.Web));
}
