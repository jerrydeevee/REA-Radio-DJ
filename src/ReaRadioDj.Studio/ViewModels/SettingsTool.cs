using ReaRadioDj.Studio.Services;
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
    public ObservableCollection<OutputRouteVm> StripOutputs { get; } = [];

    // Sound engine (active after a restart of the engine)
    public ObservableCollection<OutputChoiceDto> AsioChoices { get; } = [];
    public IReadOnlyList<int> SampleRates { get; } = [44100, 48000];
    [ObservableProperty] private bool _audioLoaded;
    [ObservableProperty] private OutputChoiceDto? _programAsio;
    [ObservableProperty] private OutputChoiceDto? _previewAsio;
    [ObservableProperty] private int _sampleRate = 44100;
    [ObservableProperty] private decimal _bufferMs = 250;
    [ObservableProperty] private decimal _asioBuffer;
    [ObservableProperty] private string _audioStatus = "";
    [ObservableProperty] private bool _limiterOn = true;
    [ObservableProperty] private decimal _limiterCeiling = -1;
    private bool _masterLoaded;
    [ObservableProperty] private string _outputStatus = "";

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
    /// <summary>0 = only warn, 1 = also start the automation, 2 = play from the emergency category, 3 = a random song.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UsesEmergencyCategory))] private int _silenceAction = 1;
    public IReadOnlyList<string> SilenceActions { get; } = ["Alleen melden in de studio", "Melden en de automatisering starten", "Melden en direct iets uit de noodcategorie spelen (lege categorie: willekeurig nummer)", "Melden en direct een willekeurig nummer uit de muziekbibliotheek spelen"];
    [ObservableProperty] private CategoryDto? _silenceCategory;
    public ObservableCollection<CategoryDto> Categories => Hub.Categories;
    public bool UsesEmergencyCategory => SilenceAction == 2;
    private static readonly string[] SilenceActionNames = ["warn", "autoplay", "category", "random"];

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

    // ---- appearance of this computer: applied at once, saved in studio.json
    public IReadOnlyList<string> ThemeNames { get; } = [.. Appearance.Themes.Select(t => t.Name)];
    public IReadOnlyList<string> CornerNames { get; } = [.. Appearance.Corners.Select(c => c.Name)];
    public IReadOnlyList<string> ScaleNames { get; } = [.. Appearance.Scales.Select(p => $"{p} %")];
    [ObservableProperty] private int _themeIndex = Math.Max(0, Array.FindIndex(Appearance.Themes, t => t.Key == StudioHub.Current.Settings.Theme));
    [ObservableProperty] private int _cornerIndex = Math.Max(0, Array.FindIndex(Appearance.Corners, c => c.Key == StudioHub.Current.Settings.Corners));
    [ObservableProperty] private int _scaleIndex = Math.Max(0, Array.IndexOf(Appearance.Scales, StudioHub.Current.Settings.ScalePercent));

    partial void OnThemeIndexChanged(int value) { if (value >= 0) { Hub.Settings.Theme = Appearance.Themes[value].Key; ApplyLook(); } }
    partial void OnCornerIndexChanged(int value) { if (value >= 0) { Hub.Settings.Corners = Appearance.Corners[value].Key; ApplyLook(); } }
    partial void OnScaleIndexChanged(int value) { if (value >= 0) { Hub.Settings.ScalePercent = Appearance.Scales[value]; ApplyLook(); } }

    [ObservableProperty] private bool _touchMode = StudioHub.Current.Settings.Touch;
    partial void OnTouchModeChanged(bool value) { Hub.Settings.Touch = value; ApplyLook(); }

    /// <summary>One row per kind of item, each with a colour to pick.</summary>
    public IReadOnlyList<KindColorRow> KindColorRows { get; } =
        [.. Appearance.ItemKinds.Select(k => new KindColorRow(k.Key, k.Name, StudioHub.Current.Settings))];

    private void ApplyLook()
    {
        Appearance.Apply(Hub.Settings);
        Hub.Settings.Save();
    }
    [ObservableProperty] private bool _startEngine;

    protected override void OnFirstShow() => _ = LoadAsync();

    // ---- mixing desk or software
    [ObservableProperty] private bool _deskMode;
    [ObservableProperty] private string _deskStatus = "";
    private bool _deskLoaded;

    private sealed record MixingMode(bool Desk, List<string> Direct);

    private async Task LoadDeskModeAsync()
    {
        if (await Hub.Client.GetAsync<MixingMode>("/api/mixing-mode") is not { } m) return;
        _deskLoaded = false;
        DeskMode = m.Desk;
        _deskLoaded = true;
        DeskStatus = Describe(m);
    }

    private static string Describe(MixingMode m) => !m.Desk ? "De engine mengt; faders, dempen en automatisch zachter werken op alle kanalen."
        : m.Direct.Count == 0 ? "Mengtafel, maar nog geen kanaal heeft een eigen uitgang: geef ze hieronder een uitgang."
        : $"Op vol naar de tafel: {string.Join(", ", m.Direct)}.";

    partial void OnDeskModeChanged(bool value)
    {
        if (!_deskLoaded) return;
        _ = SaveDeskModeAsync(value);
    }

    private async Task SaveDeskModeAsync(bool desk)
    {
        var m = await Hub.Client.SendForAsync<MixingMode>(HttpMethod.Put, "/api/mixing-mode", new { desk });
        DeskStatus = m is null ? "Omschakelen mislukt." : Describe(m);
    }

    // ---- the station's own recordings
    [ObservableProperty] private bool _recProgram;
    [ObservableProperty] private bool _recMic;
    [ObservableProperty] private bool _recMicToLibrary;
    [ObservableProperty] private int _recBitrate = 128;
    [ObservableProperty] private decimal _recKeepDays = 365;
    [ObservableProperty] private string _recFolder = "";
    [ObservableProperty] private string _recStatus = "";
    public int[] RecBitrates { get; } = [64, 96, 128, 160, 192, 256, 320];

    private async Task LoadRecordingAsync()
    {
        if (await Hub.Client.GetAsync<RecordingSettingsDto>("/api/recordings/settings") is { } r) ShowRecording(r);
    }

    private void ShowRecording(RecordingSettingsDto r)
    {
        RecProgram = r.Program; RecMic = r.Mic; RecMicToLibrary = r.MicToLibrary; RecBitrate = r.Bitrate;
        RecKeepDays = r.KeepDays; RecFolder = r.Folder;
        var now = new List<string>();
        if (r.ProgramFile is { } p) now.Add($"neemt nu op: {Path.GetFileName(p)}");
        if (r.MicFile is { } m) now.Add($"microfoon: {Path.GetFileName(m)}");
        RecStatus = (r.Error.Length > 0 ? r.Error + "  " : "") + $"Map: {r.FolderInUse}" + (now.Count > 0 ? "  ·  " + string.Join("  ·  ", now) : "");
    }

    [RelayCommand]
    private async Task SaveRecording()
    {
        var r = await Hub.Client.SendForAsync<RecordingSettingsDto>(HttpMethod.Put, "/api/recordings/settings",
            new RecordingSettingsDto(RecProgram, RecMic, RecMicToLibrary, RecBitrate, (int)RecKeepDays, RecFolder.Trim()));
        if (r is null) { RecStatus = "Opslaan mislukt."; return; }
        await Task.Delay(1500);   // the recorder starts on its next tick
        await LoadRecordingAsync();
    }

    // ---- transitions per kind of item and category
    public ObservableCollection<TransitionRuleRow> TransitionRules { get; } = [];
    public ObservableCollection<KeyChoice> RuleSides { get; } = [];
    public ObservableCollection<KeyChoice> RuleModes { get; } = [];
    [ObservableProperty] private string _ruleStatus = "";

    private sealed record RuleWire(string From, string To, string Mode, double Seconds, bool Enabled);
    private sealed record RuleEnvelope(List<RuleWire> Rules, List<KeyChoice> Sides, List<KeyChoice> Modes);

    private async Task LoadRulesAsync()
    {
        if (await Hub.Client.GetAsync<RuleEnvelope>("/api/transition-rules") is not { } e) return;
        RuleSides.Clear(); foreach (var c in e.Sides) RuleSides.Add(c);
        RuleModes.Clear(); foreach (var c in e.Modes) RuleModes.Add(c);
        TransitionRules.Clear();
        foreach (var r in e.Rules)
            TransitionRules.Add(new TransitionRuleRow
            {
                From = Side(r.From), To = Side(r.To), Mode = RuleModes.FirstOrDefault(m => m.Key == r.Mode),
                Seconds = (decimal)r.Seconds, Enabled = r.Enabled,
            });
    }

    // A category that no longer exists still shows, so the rule is not lost unnoticed.
    private KeyChoice Side(string key) => RuleSides.FirstOrDefault(s => s.Key == key) ?? AddSide(key);
    private KeyChoice AddSide(string key) { var c = new KeyChoice(key, key + " (bestaat niet meer)"); RuleSides.Add(c); return c; }

    [RelayCommand]
    private void AddRule() => TransitionRules.Add(new TransitionRuleRow
    {
        From = RuleSides.FirstOrDefault(s => s.Key == "jingle"), To = RuleSides.FirstOrDefault(s => s.Key == "song"),
        Mode = RuleModes.FirstOrDefault(m => m.Key == "cut"), Seconds = 2,
    });

    [RelayCommand] private void RemoveRule(TransitionRuleRow? row) { if (row is not null) TransitionRules.Remove(row); }

    [RelayCommand]
    private void MoveRuleUp(TransitionRuleRow? row)
    {
        if (row is null || TransitionRules.IndexOf(row) is not (> 0 and var i)) return;
        TransitionRules.Move(i, i - 1);
    }

    [RelayCommand]
    private async Task SaveRules()
    {
        var body = TransitionRules.Where(r => r.From is not null && r.To is not null && r.Mode is not null)
            .Select(r => new RuleWire(r.From!.Key, r.To!.Key, r.Mode!.Key, (double)r.Seconds, r.Enabled)).ToList();
        var saved = await Hub.Client.SendForAsync<List<RuleWire>>(HttpMethod.Put, "/api/transition-rules", body);
        RuleStatus = saved is null ? "Opslaan mislukt." : $"{saved.Count} overgangsregels bewaard; ze gelden vanaf het volgende item.";
        await LoadRulesAsync();
        await LoadRecordingAsync();
    }

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
        await LoadRulesAsync();
        await LoadSystemAsync();
        await LoadOutputsAsync();
        await LoadDeskModeAsync();
        await LoadAudioAsync();
        if (await Hub.Client.GetAsync<MasterDto>("/api/master") is { } m) { LimiterOn = m.LimiterOn; LimiterCeiling = (decimal)m.CeilingDb; _masterLoaded = true; }
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

    private async Task LoadAudioAsync()
    {
        var a = await Hub.Client.GetAsync<AudioSettingsDto>("/api/audio");
        AudioLoaded = a is not null;
        if (a is null) { AudioStatus = "De engine kent ASIO en buffers nog niet: werk hem bij."; return; }
        ShowAudio(a);
    }

    private void ShowAudio(AudioSettingsDto a)
    {
        AsioChoices.Clear();
        foreach (var c in a.AsioChoices) AsioChoices.Add(c);
        ProgramAsio = AsioChoices.FirstOrDefault(c => c.Id == a.ProgramAsio) ?? AsioChoices[0];
        PreviewAsio = AsioChoices.FirstOrDefault(c => c.Id == a.PreviewAsio) ?? AsioChoices[0];
        SampleRate = a.SampleRate;
        BufferMs = a.BufferMs;
        AsioBuffer = a.AsioBuffer;
        var notes = new List<string>();
        if (!a.AsioAvailable) notes.Add("bassasio.dll ontbreekt: draai scripts\\fetch-bass.ps1 en bouw opnieuw.");
        else if (a.AsioChoices.Count <= 1) notes.Add("Geen ASIO-stuurprogramma gevonden op de pc van de engine.");
        if (a.ProgramNow.Length > 0) notes.Add($"Nu: {a.ProgramNow} en {a.PreviewNow}.");
        if (a.NeedsRestart) notes.Add("Gewijzigd: actief na een herstart van de engine (Instellingen › Systeem).");
        AudioStatus = string.Join(" ", notes);
    }

    private async Task LoadOutputsAsync()
    {
        StripOutputs.Clear();
        var o = await Hub.Client.GetAsync<OutputsDto>("/api/outputs");
        if (o is null) { OutputStatus = "De engine kent uitgangen per kanaal nog niet: werk hem bij."; return; }
        foreach (var s in o.Strips) StripOutputs.Add(new OutputRouteVm(this, s, o.Targets));
    }

    internal async Task RouteAsync(OutputRouteVm row, OutputChoiceDto target)
    {
        OutputStatus = $"{row.Name} omzetten…";
        var r = await Hub.Client.SendForAsync<OutputsDto>(HttpMethod.Put, $"/api/outputs/{row.Strip}", new OutputRouteRequest(target.Id));
        var now = r?.Strips.FirstOrDefault(s => s.Strip == row.Strip);
        row.Show(now?.Target ?? "");
        OutputStatus = r is null ? $"{row.Name}: deze uitgang werkt niet (zie de statusregel)." :
            string.IsNullOrEmpty(now?.Target) ? $"{row.Name} gaat via de normale uitgang." : $"{row.Name} heeft nu een eigen uitgang.";
    }

    internal async Task TestToneAsync(string output)
    {
        OutputStatus = await Hub.Client.PostAsync("/api/outputs/test", new OutputRouteRequest(output))
            ? "Testtoon: 1 kHz, -18 dBFS, anderhalve seconde." : "Testtoon niet gelukt.";
    }

    [RelayCommand] private Task TestProgram() => TestToneAsync("program");
    [RelayCommand] private async Task ResetLoudness() => OutputStatus = await Hub.Client.PostAsync("/api/master/reset") ? "Geïntegreerde loudness begint opnieuw." : "";
    [RelayCommand] private Task TestPreview() => TestToneAsync("preview");

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
        SilenceEnabled = s.SilenceEnabled; SilenceDb = (decimal)s.SilenceDb; SilenceSeconds = (decimal)s.SilenceSeconds; SilenceAction = Math.Max(0, Array.IndexOf(SilenceActionNames, s.SilenceAction)); SilenceCategory = Hub.Categories.FirstOrDefault(c => c.Id == s.SilenceCategoryId);
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
            SilenceEnabled, (double)SilenceDb, (double)SilenceSeconds, SilenceActionNames[Math.Clamp(SilenceAction, 0, SilenceActionNames.Length - 1)], SilenceCategory?.Id));

        if (_masterLoaded) await Hub.Client.PutAsync("/api/master", new MasterDto(LimiterOn, (double)LimiterCeiling));

        if (AudioLoaded)
        {
            var a = await Hub.Client.SendForAsync<AudioSettingsDto>(HttpMethod.Put, "/api/audio",
                new AudioSettingsRequest(ProgramAsio?.Id ?? "", PreviewAsio?.Id ?? "", SampleRate, (int)BufferMs, (int)AsioBuffer));
            if (a is not null) ShowAudio(a);
        }

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

/// <summary>A choice with a key for the engine and a Dutch label.</summary>
public sealed record KeyChoice(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One transition rule in the settings: from this kind or category into that one, this way.</summary>
public sealed partial class TransitionRuleRow : ObservableObject
{
    [ObservableProperty] private KeyChoice? _from;
    [ObservableProperty] private KeyChoice? _to;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSeconds))] private KeyChoice? _mode;
    [ObservableProperty] private decimal _seconds = 2;
    [ObservableProperty] private bool _enabled = true;
    public bool HasSeconds => Mode?.Key is "overlap" or "fade" or "tempo";
}

/// <summary>Instellingen › Uiterlijk: the colour of one kind of item (muziek, jingles, ...), applied and saved at once.</summary>
public sealed partial class KindColorRow : ObservableObject
{
    private readonly string _key;
    private readonly StudioSettings _settings;

    public KindColorRow(string key, string name, StudioSettings settings)
    {
        _key = key;
        _settings = settings;
        Name = name;
        var hex = settings.TypeColors.TryGetValue(key, out var h) ? h : Appearance.DefaultTypeColors[key];
        _colorIndex = Math.Max(0, Array.FindIndex(Appearance.KindColors, c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)));
    }

    public string Name { get; }
    public IReadOnlyList<KindColorChoice> Choices { get; } = [.. Appearance.KindColors.Select(c => new KindColorChoice(c.Name, Brushes.FromHex(c.Hex)))];

    [ObservableProperty] private int _colorIndex;

    partial void OnColorIndexChanged(int value)
    {
        if (value < 0) return;
        _settings.TypeColors[_key] = Appearance.KindColors[value].Hex;
        Appearance.Apply(_settings);
        _settings.Save();
    }
}

public sealed record KindColorChoice(string Name, Avalonia.Media.IBrush Brush);
