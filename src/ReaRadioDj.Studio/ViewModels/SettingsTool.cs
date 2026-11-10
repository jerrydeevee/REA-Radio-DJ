using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// One window for the settings of the station: name and folders, sound cards, mixing, rotation, the stream, remote
/// control and this studio computer. Everything is read from the engine when the window opens and written with Opslaan.
/// </summary>
public sealed partial class SettingsTool : StudioTool
{
    public SettingsTool() : base("settings", "Instellingen") { }

    public ObservableCollection<DeviceDto> Outputs { get; } = [];
    public ObservableCollection<DeviceDto> Inputs { get; } = [];
    public IReadOnlyList<int> Bitrates { get; } = [64, 96, 128, 160, 192, 256, 320];

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _loaded;

    // Station
    [ObservableProperty] private string _stationName = "";
    [ObservableProperty] private string _musicFolder = "";
    [ObservableProperty] private string _jingleFolder = "";
    [ObservableProperty] private bool _startAutomation;

    // Sound
    [ObservableProperty] private DeviceDto? _outputDevice;
    [ObservableProperty] private DeviceDto? _previewDevice;
    [ObservableProperty] private DeviceDto? _micDevice;
    [ObservableProperty] private bool _micEnabled;
    [ObservableProperty] private decimal _micDuck = 25;
    [ObservableProperty] private decimal _overlayDuck = 35;
    [ObservableProperty] private decimal _standbyPlayers = 2;

    // Mixing
    [ObservableProperty] private decimal _targetLufs = -16;
    [ObservableProperty] private decimal _cueInDb = -40;
    [ObservableProperty] private decimal _mixDb = -24;
    [ObservableProperty] private decimal _cueOutDb = -50;
    [ObservableProperty] private decimal _maxOverlap = 8;
    [ObservableProperty] private decimal _fadeOut = 2.5m;
    [ObservableProperty] private decimal _duckLevel = 30;

    // Guarding the broadcast
    [ObservableProperty] private bool _silenceEnabled = true;
    [ObservableProperty] private decimal _silenceDb = -45;
    [ObservableProperty] private decimal _silenceSeconds = 20;
    /// <summary>0 = only warn, 1 = also start the automation.</summary>
    [ObservableProperty] private int _silenceAction = 1;
    public IReadOnlyList<string> SilenceActions { get; } = ["Alleen melden in de studio", "Melden en de automatisering starten"];

    // Rotation
    [ObservableProperty] private decimal _artistSeparation = 3;
    [ObservableProperty] private decimal _songSeparation = 10;
    [ObservableProperty] private decimal _lookAhead = 8;

    // Stream
    [ObservableProperty] private bool _streamEnabled;
    [ObservableProperty] private string _streamServer = "";
    [ObservableProperty] private string _streamPassword = "";
    [ObservableProperty] private string _streamPasswordHint = "";
    [ObservableProperty] private int _streamBitrate = 128;
    [ObservableProperty] private string _streamGenre = "";
    [ObservableProperty] private string _streamUrl = "";

    // Remote control
    [ObservableProperty] private string _controlBindAddress = "127.0.0.1";
    [ObservableProperty] private decimal _controlTcpPort = 8601;
    [ObservableProperty] private decimal _controlOscPort = 8602;
    [ObservableProperty] private string _controlPassword = "";
    [ObservableProperty] private string _controlPasswordHint = "";
    [ObservableProperty] private bool _clearControlPassword;

    // Keeping the station running
    [ObservableProperty] private bool _systemLoaded;
    [ObservableProperty] private bool _watched;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _detailedLog;
    [ObservableProperty] private string _watchText = "";
    [ObservableProperty] private string _autoStartCommand = "";
    [ObservableProperty] private string _watchdogLog = "";
    [ObservableProperty] private string _backupStatus = "";
    [ObservableProperty] private bool _showInfoLines;
    public ObservableCollection<SystemLogEntryDto> SystemLines { get; } = [];

    // This studio computer
    [ObservableProperty] private string _engineUrl = "";
    [ObservableProperty] private bool _startEngine;

    protected override void OnFirstShow() => _ = LoadAsync();

    [RelayCommand] private Task Reload() => LoadAsync();

    private async Task LoadAsync()
    {
        Status = "lezen…";
        var s = await Hub.Client.GetAsync<SettingsDto>("/api/settings");
        var outs = await Hub.Client.GetAsync<List<DeviceDto>>("/api/devices");
        var ins = await Hub.Client.GetAsync<List<DeviceDto>>("/api/record-devices");
        var r = await Hub.Client.GetAsync<RoutingDto>("/api/routing");
        EngineUrl = Hub.Settings.EngineUrl;
        StartEngine = Hub.Settings.StartEngine;
        if (s is null || outs is null || ins is null || r is null)
        {
            Loaded = false;
            Status = "De engine geeft geen instellingen: is hij verbonden en bijgewerkt?";
            return;
        }
        Show(s);
        await LoadSystemAsync();
        Outputs.Clear();
        Outputs.Add(new DeviceDto(-1, "Standaard geluidskaart van Windows", true, false));
        foreach (var d in outs) Outputs.Add(d);
        Inputs.Clear();
        Inputs.Add(new DeviceDto(-1, "Standaard invoer van Windows", true, false));
        foreach (var d in ins) Inputs.Add(d);
        OutputDevice = Outputs.FirstOrDefault(d => d.Index == r.OutputDevice);
        PreviewDevice = Outputs.FirstOrDefault(d => d.Index == r.PreviewDevice);
        MicDevice = Inputs.FirstOrDefault(d => d.Index == r.MicDevice);
        MicEnabled = r.MicEnabled;
        MicDuck = Percent(r.MicDuckLevel);
        OverlayDuck = Percent(r.OverlayDuckLevel);
        Loaded = true;
        Status = "";
    }

    private async Task LoadSystemAsync()
    {
        var sys = await Hub.Client.GetAsync<SystemDto>("/api/system");
        SystemLoaded = sys is not null;
        if (sys is null) { WatchText = "De engine kent deze instellingen nog niet: werk hem bij."; return; }
        Watched = sys.Watched;
        AutoStart = sys.AutoStart;
        DetailedLog = sys.DetailedLog;
        AutoStartCommand = sys.AutoStartCommand ?? "";
        WatchText = sys.Watched
            ? "De engine draait met bewaking: na een crash of vastloper start hij binnen enkele seconden opnieuw en gaat hij verder waar hij was."
            : "De engine draait zonder bewaking. Start hem met --watchdog (de studio doet dat zelf als zij de engine start), of zet hieronder starten bij aanmelden aan.";
        WatchdogLog = sys.WatchdogLog.Count == 0 ? "(nog niets)" : string.Join(Environment.NewLine, sys.WatchdogLog.Reverse());
        if (sys.RestorePending) BackupStatus = "Er staat een reservekopie klaar die bij de volgende start van de engine wordt teruggezet.";
        await RefreshLog();
    }

    [RelayCommand]
    private async Task RefreshLog()
    {
        var lines = await Hub.Client.GetAsync<List<SystemLogEntryDto>>($"/api/system/log?detailed={(ShowInfoLines ? "true" : "false")}");
        SystemLines.Clear();
        if (lines is null) return;
        foreach (var l in Enumerable.Reverse(lines).Take(500)) SystemLines.Add(l);
    }

    partial void OnShowInfoLinesChanged(bool value) => _ = RefreshLog();

    [RelayCommand]
    private async Task RestartEngine()
    {
        BackupStatus = await Hub.Client.PostAsync("/api/system/restart")
            ? "De engine start opnieuw; de studio maakt vanzelf weer verbinding."
            : "Opnieuw starten kan alleen als de engine met bewaking draait.";
    }

    /// <summary>Fetches a backup of the whole station from the engine.</summary>
    public async Task<byte[]?> FetchBackupAsync()
    {
        BackupStatus = "reservekopie maken…";
        var data = await Hub.Client.GetBytesAsync("/api/backup");
        if (data is null) BackupStatus = "De engine maakte geen reservekopie.";
        return data;
    }

    /// <summary>Sends a backup to the engine; it is put back when the engine starts again.</summary>
    public async Task RestoreBackupAsync(byte[] data)
    {
        BackupStatus = "reservekopie versturen…";
        var (r, error) = await Hub.Client.PostBytesAsync<BackupStagedDto>("/api/backup", data);
        BackupStatus = r is null
            ? $"Niet teruggezet: {error}"
            : $"Reservekopie van {r.Station} ({r.Created:d-M-yyyy HH:mm}, {r.Machine}) "
              + (r.Restarting ? "wordt nu teruggezet; de engine start opnieuw." : "wordt teruggezet als je de engine opnieuw start.");
    }

    private static decimal Percent(double level) => Math.Round((decimal)level * 100);

    private void Show(SettingsDto s)
    {
        StationName = s.StationName; MusicFolder = s.MusicFolder; JingleFolder = s.JingleFolder; StartAutomation = s.StartAutomation;
        StandbyPlayers = s.StandbyPlayers;
        TargetLufs = (decimal)s.TargetLufs; CueInDb = (decimal)s.CueInDb; MixDb = (decimal)s.MixDb; CueOutDb = (decimal)s.CueOutDb;
        MaxOverlap = (decimal)s.MaxOverlap; FadeOut = (decimal)s.FadeOut; DuckLevel = Percent(s.DuckLevel);
        ArtistSeparation = s.ArtistSeparation; SongSeparation = s.SongSeparation; LookAhead = s.LookAhead;
        SilenceEnabled = s.SilenceEnabled; SilenceDb = (decimal)s.SilenceDb; SilenceSeconds = (decimal)s.SilenceSeconds; SilenceAction = s.SilenceAction == "warn" ? 0 : 1;
        StreamEnabled = s.StreamEnabled; StreamServer = s.StreamServer; StreamBitrate = s.StreamBitrate; StreamGenre = s.StreamGenre; StreamUrl = s.StreamUrl;
        StreamPassword = "";
        StreamPasswordHint = s.StreamPasswordSet ? "er is een wachtwoord opgeslagen; leeg laten = houden" : "nog geen wachtwoord";
        ControlBindAddress = s.ControlBindAddress; ControlTcpPort = s.ControlTcpPort; ControlOscPort = s.ControlOscPort;
        ControlPassword = "";
        ClearControlPassword = false;
        ControlPasswordHint = s.ControlPasswordSet ? "er is een wachtwoord opgeslagen; leeg laten = houden" : "geen wachtwoord: iedereen op het netwerk mag bedienen";
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!Loaded) return;
        Status = "opslaan…";
        var routing = new RoutingDto(OutputDevice?.Index ?? -1, PreviewDevice?.Index ?? -1, MicEnabled, MicDevice?.Index ?? -1,
            (double)MicDuck / 100, (double)OverlayDuck / 100);
        bool routed = await Hub.Client.PutAsync("/api/routing", routing);
        var saved = await Hub.Client.SendForAsync<SettingsSavedDto>(HttpMethod.Put, "/api/settings", new SettingsDto(
            StationName, MusicFolder, JingleFolder, StartAutomation, (int)StandbyPlayers,
            (double)TargetLufs, (double)CueInDb, (double)MixDb, (double)CueOutDb, (double)MaxOverlap, (double)FadeOut, (double)DuckLevel / 100,
            (int)ArtistSeparation, (int)SongSeparation, (int)LookAhead,
            StreamEnabled, StreamServer, StreamPassword, false, StreamBitrate, StreamGenre, StreamUrl,
            ControlBindAddress, (int)ControlTcpPort, (int)ControlOscPort, ControlPassword, false, ClearControlPassword,
            SilenceEnabled, (double)SilenceDb, (double)SilenceSeconds, SilenceAction == 0 ? "warn" : "autoplay"));

        if (SystemLoaded)
        {
            var sys = await Hub.Client.SendForAsync<SystemDto>(HttpMethod.Put, "/api/system", new SystemSettingsRequest(AutoStart, DetailedLog));
            if (sys is not null) { AutoStart = sys.AutoStart; AutoStartCommand = sys.AutoStartCommand ?? ""; }
        }

        // This computer: the engine address is used the next time the studio starts.
        Hub.Settings.EngineUrl = string.IsNullOrWhiteSpace(EngineUrl) ? Hub.Settings.EngineUrl : EngineUrl.Trim();
        Hub.Settings.StartEngine = StartEngine;
        Hub.Settings.Save();

        if (saved is null) { Status = "Opslaan mislukt: de engine nam de instellingen niet aan."; return; }
        Show(saved.Settings);
        var notes = new List<string> { "Opgeslagen." };
        if (!routed) notes.Add("De geluidskaarten zijn niet gewisseld (kaart niet beschikbaar?).");
        if (saved.Rescanning) notes.Add("De bibliotheek wordt opnieuw ingelezen uit de nieuwe mappen.");
        if (saved.NeedsRestart.Count > 0) notes.Add("Actief na een herstart van de engine: " + string.Join(", ", saved.NeedsRestart) + ".");
        Status = string.Join(" ", notes);
    }
}
