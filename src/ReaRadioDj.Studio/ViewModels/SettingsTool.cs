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
    /// <summary>The longest a soft fixed-time event waits for the item on air (0 = always wait).</summary>
    [ObservableProperty] private decimal _softMaxWait;
    private bool _softLoaded;
    private sealed record SoftStartWire(double MaxWaitSeconds);

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

    // Spotify (developer keys, metadata only)
    [ObservableProperty] private bool _spotifyLoaded;
    [ObservableProperty] private string _spotifyClientId = "";
    [ObservableProperty] private string _spotifySecret = "";
    [ObservableProperty] private string _spotifySecretHint = "";
    [ObservableProperty] private string _spotifyMarket = "NL";
    [ObservableProperty] private string _spotifyStatus = "";
    [ObservableProperty] private bool _spotifyOk;
    [ObservableProperty] private bool _spotifyFailed;
    [ObservableProperty] private bool _spotifyBusy;
    private SpotifySettingsDto? _spotify;

    // AcoustID (recognition by sound; the key lives in the engine)
    [ObservableProperty] private bool _acoustIdLoaded;
    [ObservableProperty] private string _acoustIdKey = "";
    [ObservableProperty] private string _acoustIdKeyHint = "";
    [ObservableProperty] private string _acoustIdFpcalc = "";
    [ObservableProperty] private string _acoustIdStatus = "";
    [ObservableProperty] private bool _acoustIdOk;
    [ObservableProperty] private bool _acoustIdFailed;
    [ObservableProperty] private bool _acoustIdBusy;
    private string _acoustIdRegisterUrl = "https://acoustid.org/new-application";

    // Spotify Soloist on this computer (studio settings, not the engine)
    [ObservableProperty] private string _soloistPath = "";
    [ObservableProperty] private string _soloistDevice = "REA ontwikkeltest";
    [ObservableProperty] private decimal _soloistPort = 9090;
    [ObservableProperty] private string _soloistKey = "";
    [ObservableProperty] private string _soloistKeyHint = "";
    [ObservableProperty] private bool _soloistStartWithStudio;

    /// <summary>Instellingen › Systeem › Nieuwe versies: a studio setting, saved at once.</summary>
    public bool CheckUpdates
    {
        get => Hub.Settings.CheckUpdates;
        set
        {
            if (Hub.Settings.CheckUpdates == value) return;
            Hub.Settings.CheckUpdates = value;
            Hub.Settings.Save();
            OnPropertyChanged();
        }
    }
    [ObservableProperty] private bool _soloistAutoConnect = true;
    [ObservableProperty] private string _soloistStatus = "";
    [ObservableProperty] private bool _soloistRunning;
    [ObservableProperty] private string _soloistFound = "";

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
    // the daily backup
    [ObservableProperty] private bool _autoBackupLoaded;
    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private string _autoBackupFolder = "";
    [ObservableProperty] private string _autoBackupTime = "03:00";
    [ObservableProperty] private decimal _autoBackupKeep = 7;
    [ObservableProperty] private string _autoBackupInfo = "";
    [ObservableProperty] private string _autoBackupFiles = "";
    // ports
    public ObservableCollection<PortDto> Ports { get; } = [];
    [ObservableProperty] private string _firewallCommand = "";
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

    // Accent and waveform colours: "Van de stijl" (empty) or one of the kind colours.
    public IReadOnlyList<LookColorChoice> AccentChoices { get; } =
        [new LookColorChoice("Van de stijl", "", Avalonia.Media.Brushes.Transparent), .. Appearance.KindColors.Select(c => new LookColorChoice(c.Name, c.Hex, Brushes.FromHex(c.Hex)))];
    public IReadOnlyList<string> ButtonStyleNames { get; } = [.. Appearance.ButtonStyles.Select(b => b.Name)];
    public IReadOnlyList<string> WaveStyleNames { get; } = [.. Appearance.WaveStyles.Select(w => w.Name)];
    [ObservableProperty] private int _accentIndex = ChoiceIndex(StudioHub.Current.Settings.Accent);
    [ObservableProperty] private int _waveColorIndex = ChoiceIndex(StudioHub.Current.Settings.WaveColor);
    [ObservableProperty] private int _buttonStyleIndex = Math.Max(0, Array.FindIndex(Appearance.ButtonStyles, b => b.Key == StudioHub.Current.Settings.ButtonStyle));
    [ObservableProperty] private int _waveStyleIndex = Math.Max(0, Array.FindIndex(Appearance.WaveStyles, w => w.Key == StudioHub.Current.Settings.WaveStyle));

    private static int ChoiceIndex(string hex) =>
        string.IsNullOrWhiteSpace(hex) ? 0 : Array.FindIndex(Appearance.KindColors, c => string.Equals(c.Hex, hex, StringComparison.OrdinalIgnoreCase)) + 1;

    partial void OnAccentIndexChanged(int value) { if (value >= 0) { Hub.Settings.Accent = AccentChoices[value].Hex; ApplyLook(); } }
    partial void OnWaveColorIndexChanged(int value) { if (value >= 0) { Hub.Settings.WaveColor = AccentChoices[value].Hex; ApplyLook(); } }
    partial void OnButtonStyleIndexChanged(int value) { if (value >= 0) { Hub.Settings.ButtonStyle = Appearance.ButtonStyles[value].Key; ApplyLook(); } }
    partial void OnWaveStyleIndexChanged(int value) { if (value >= 0) { Hub.Settings.WaveStyle = Appearance.WaveStyles[value].Key; ApplyLook(); } }

    [ObservableProperty] private bool _touchMode = StudioHub.Current.Settings.Touch;
    partial void OnTouchModeChanged(bool value) { Hub.Settings.Touch = value; ApplyLook(); }
    [ObservableProperty] private bool _calmColors = StudioHub.Current.Settings.CalmColors;
    partial void OnCalmColorsChanged(bool value) { Hub.Settings.CalmColors = value; ApplyLook(); }

    /// <summary>One row per kind of item, each with a colour to pick.</summary>
    public IReadOnlyList<KindColorRow> KindColorRows { get; } =
        [.. Appearance.ItemKinds.Select(k => new KindColorRow(k.Key, k.Name, StudioHub.Current.Settings))];

    private void ApplyLook()
    {
        Appearance.Apply(Hub.Settings);
        Hub.Settings.Save();
    }
    [ObservableProperty] private bool _startEngine;

    protected override void OnFirstShow() { _ = LoadAsync(); _ = Users.LoadAsync(); _ = Remotes.LoadAsync(); }

    /// <summary>Instellingen › Gebruikers.</summary>
    public UsersPanelVm Users { get; } = new();
    public RemoteClientsVm Remotes { get; } = new();

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

    // The two choices as their own properties that only ever switch ON. A radio button bound to "!DeskMode" wrote
    // back when the group unchecked it while the look was applied again (a new button style re-templates every button):
    // the two buttons then flipped DeskMode back and forth without end, froze the studio and flooded the engine.
    public bool DeskSoftware { get => !DeskMode; set { if (value) DeskMode = false; } }
    public bool DeskTable { get => DeskMode; set { if (value) DeskMode = true; } }

    partial void OnDeskModeChanged(bool value)
    {
        OnPropertyChanged(nameof(DeskSoftware));
        OnPropertyChanged(nameof(DeskTable));
        if (!_deskLoaded) return;
        _ = SaveDeskModeAsync(value);
    }

    private bool _deskSending;
    private bool? _deskWanted;

    /// <summary>One request at a time; quick changes in between collapse into the last one.</summary>
    private async Task SaveDeskModeAsync(bool desk)
    {
        _deskWanted = desk;
        if (_deskSending) return;
        _deskSending = true;
        try
        {
            while (_deskWanted is bool want)
            {
                _deskWanted = null;
                var m = await Hub.Client.SendForAsync<MixingMode>(HttpMethod.Put, "/api/mixing-mode", new { desk = want });
                DeskStatus = m is null ? "Omschakelen mislukt." : Describe(m);
            }
        }
        finally { _deskSending = false; }
    }

    // ---- the station's own recordings
    [ObservableProperty] private bool _recProgram;
    [ObservableProperty] private bool _recMic;
    [ObservableProperty] private bool _recMicToLibrary;
    [ObservableProperty] private int _recBitrate = 128;
    [ObservableProperty] private decimal _recKeepDays = 365;
    [ObservableProperty] private string _recFolder = "";
    [ObservableProperty] private string _recStatus = "";
    [ObservableProperty] private bool _recSpeech;
    [ObservableProperty] private decimal _recSpeechThreshold = -40;
    [ObservableProperty] private decimal _recSpeechHang = 2;
    private static readonly OutputChoiceDto MicChoice = new("mic", "Microfoon");
    [ObservableProperty] private List<OutputChoiceDto> _recSpeechSources = [MicChoice];
    [ObservableProperty] private OutputChoiceDto? _recSpeechSource = MicChoice;
    public int[] RecBitrates { get; } = [64, 96, 128, 160, 192, 256, 320];

    // ---- the sound card watch (Windows volume and mute of the cards the station plays on)
    [ObservableProperty] private bool _devWatchEnabled = true;
    [ObservableProperty] private bool _devWatchRestore;
    [ObservableProperty] private List<string> _devWatchCards = [];
    [ObservableProperty] private string _devWatchStatus = "";

    private async Task LoadDeviceWatchAsync()
    {
        if (await Hub.Client.GetAsync<DeviceWatchDto>("/api/devicewatch") is { } d) ShowDeviceWatch(d);
    }

    private void ShowDeviceWatch(DeviceWatchDto d)
    {
        DevWatchEnabled = d.Enabled;
        DevWatchRestore = d.Restore;
        DevWatchCards = d.Cards.Select(c =>
            $"{c.Name} — {c.Uses}: {(c.Volume is double v ? (c.Muted ? "gedempt" : $"{v:0}%") : "volume niet uit te lezen")}{(c.Note.Length > 0 && c.Volume is not null ? $" ({c.Note})" : "")}").ToList();
        if (DevWatchCards.Count == 0) DevWatchCards = ["Geen Windows-kaart in gebruik (ASIO of geen geluid)."];
    }

    [RelayCommand]
    private async Task SaveDeviceWatch()
    {
        var d = await Hub.Client.SendForAsync<DeviceWatchDto>(HttpMethod.Put, "/api/devicewatch", new DeviceWatchDto(DevWatchEnabled, DevWatchRestore, []));
        DevWatchStatus = d is null ? "Opslaan mislukt." : "Bewaard.";
        if (d is not null) ShowDeviceWatch(d);
    }

    [RelayCommand]
    private async Task AcceptDeviceWatch()
    {
        var d = await Hub.Client.SendForAsync<DeviceWatchDto>(HttpMethod.Post, "/api/devicewatch/accept", new { });
        DevWatchStatus = d is null ? "Mislukt." : "De huidige stand van de kaarten is nu de normale stand.";
        if (d is not null) ShowDeviceWatch(d);
    }

    private async Task LoadRecordingAsync()
    {
        if (await Hub.Client.GetAsync<RecordingSettingsDto>("/api/recordings/settings") is { } r) ShowRecording(r);
    }

    private void ShowRecording(RecordingSettingsDto r)
    {
        RecProgram = r.Program; RecMic = r.Mic; RecMicToLibrary = r.MicToLibrary; RecBitrate = r.Bitrate;
        RecKeepDays = r.KeepDays; RecFolder = r.Folder;
        // the microphone and the input channels (a second microphone on a line input, say)
        var sources = new List<OutputChoiceDto> { MicChoice };
        if (Hub.State is { } st)
            sources.AddRange(st.Mixer.Strips.Where(x => x.Source is not null).Select(x => new OutputChoiceDto(x.Id, x.Name)));
        if (sources.All(x => x.Id != r.SpeechSource)) sources.Add(new(r.SpeechSource, r.SpeechSource + " (niet gevonden)"));
        RecSpeechSources = sources;
        RecSpeechSource = sources.First(x => x.Id == r.SpeechSource);
        RecSpeech = r.Speech; RecSpeechThreshold = (decimal)r.SpeechThresholdDb; RecSpeechHang = (decimal)r.SpeechHangSeconds;
        var now = new List<string>();
        if (r.ProgramFile is { } p) now.Add($"neemt nu op: {Path.GetFileName(p)}");
        if (r.MicFile is { } m) now.Add($"microfoon: {Path.GetFileName(m)}");
        if (r.Speech)
            now.Add(r.SpeechFile is { } sf
                ? $"spraak: {Path.GetFileName(sf)}{(r.SpeechRecording ? " (neemt nu op)" : "")}, {r.SpeechTakes} keer"
                : "spraak: wacht op de microfoon");
        RecStatus = (r.Error.Length > 0 ? r.Error + "  " : "") + $"Map: {r.FolderInUse}" + (now.Count > 0 ? "  ·  " + string.Join("  ·  ", now) : "");
    }

    [RelayCommand]
    private async Task SaveRecording()
    {
        var r = await Hub.Client.SendForAsync<RecordingSettingsDto>(HttpMethod.Put, "/api/recordings/settings",
            new RecordingSettingsDto(RecProgram, RecMic, RecMicToLibrary, RecBitrate, (int)RecKeepDays, RecFolder.Trim(),
                Speech: RecSpeech, SpeechSource: RecSpeechSource?.Id ?? "mic", SpeechThresholdDb: (double)RecSpeechThreshold, SpeechHangSeconds: (double)RecSpeechHang));
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
        await LoadLimitsAsync();
        if (await Hub.Client.GetAsync<SoftStartWire>("/api/soft-start") is { } soft) { SoftMaxWait = (decimal)soft.MaxWaitSeconds; _softLoaded = true; }
    }

    // ---- longest play time per kind of item and category
    public ObservableCollection<PlayLimitRow> PlayLimits { get; } = [];
    [ObservableProperty] private string _limitStatus = "";

    private sealed record LimitWire(string Side, double MaxSeconds, double NoFadeUnder, bool Enabled);

    private async Task LoadLimitsAsync()
    {
        if (await Hub.Client.GetAsync<List<LimitWire>>("/api/play-limits") is not { } list) return;
        PlayLimits.Clear();
        foreach (var l in list)
            PlayLimits.Add(new PlayLimitRow
            {
                Side = Side(l.Side), MaxSeconds = (decimal)l.MaxSeconds, NoFadeUnder = (decimal)l.NoFadeUnder, Enabled = l.Enabled,
            });
    }

    [RelayCommand]
    private void AddLimit() => PlayLimits.Add(new PlayLimitRow { Side = RuleSides.FirstOrDefault(s => s.Key == "jingle"), MaxSeconds = 30 });

    [RelayCommand] private void RemoveLimit(PlayLimitRow? row) { if (row is not null) PlayLimits.Remove(row); }

    [RelayCommand]
    private void MoveLimitUp(PlayLimitRow? row)
    {
        if (row is null || PlayLimits.IndexOf(row) is not (> 0 and var i)) return;
        PlayLimits.Move(i, i - 1);
    }

    [RelayCommand]
    private async Task SaveLimits()
    {
        var body = PlayLimits.Where(l => l.Side is not null)
            .Select(l => new LimitWire(l.Side!.Key, (double)l.MaxSeconds, (double)l.NoFadeUnder, l.Enabled)).ToList();
        var saved = await Hub.Client.SendForAsync<List<LimitWire>>(HttpMethod.Put, "/api/play-limits", body);
        LimitStatus = saved is null ? "Opslaan mislukt." : $"{saved.Count} speelduurregels bewaard; ze gelden vanaf het volgende item.";
        await LoadLimitsAsync();
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

    /// <summary>Settings › Reservestudio.</summary>
    public FailoverVm Failover { get; } = new();

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
        await LoadDeviceWatchAsync();
        await LoadSystemAsync();
        await Failover.LoadAsync();
        await LoadOutputsAsync();
        await LoadDeskModeAsync();
        await LoadAudioAsync();
        await LoadSpotifyAsync();
        await LoadAcoustIdAsync();
        LoadSoloist();
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
        ShowAutoBackup(await Hub.Client.GetAsync<AutoBackupDto>("/api/backup/auto"));
        await RefreshPorts();
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

    [RelayCommand]
    private async Task RefreshPorts()
    {
        var p = await Hub.Client.GetAsync<PortsDto>("/api/system/ports");
        Ports.Clear();
        if (p is null) { FirewallCommand = "De engine kent het poortenoverzicht nog niet: werk hem bij."; return; }
        foreach (var port in p.Ports) Ports.Add(port);
        FirewallCommand = p.Firewall.Length > 0 ? p.Firewall
            : "Geen poort staat open voor het netwerk: alles luistert alleen op deze pc, dus er is geen firewallregel nodig.";
    }

    private void ShowAutoBackup(AutoBackupDto? a)
    {
        AutoBackupLoaded = a is not null;
        if (a is null) { AutoBackupInfo = "De engine kent de dagelijkse reservekopie nog niet: werk hem bij."; AutoBackupFiles = ""; return; }
        AutoBackupEnabled = a.Enabled;
        AutoBackupFolder = a.Folder;
        AutoBackupTime = a.Time;
        AutoBackupKeep = a.KeepDays;
        var lines = new List<string> { $"Map: {a.UsedFolder}" };
        if (a.SameDisk) lines.Add("Let op: deze map staat op dezelfde schijf als de database. Gaat die schijf kapot, dan ben je beide kwijt; kies een andere schijf of een NAS.");
        if (a.Next is { } next) lines.Add($"Volgende: {next:dddd d MMMM HH:mm}" + (a.Files.Count == 0 ? " (of binnen een minuut als er nog geen is)" : ""));
        if (a.LastError is { } e) lines.Add($"Laatste poging mislukt: {e}");
        AutoBackupInfo = string.Join(Environment.NewLine, lines);
        AutoBackupFiles = a.Files.Count == 0 ? "(nog geen automatische reservekopieën)"
            : string.Join(Environment.NewLine, a.Files.Select(f => $"{f.Time:ddd d-M-yyyy HH:mm}   {f.Bytes / 1024.0 / 1024.0:0.0} MB   {f.Name}"));
    }

    [RelayCommand]
    private async Task RunAutoBackup()
    {
        AutoBackupInfo = "reservekopie maken…";
        var (r, error) = await Hub.Client.SendWithErrorAsync<AutoBackupDto>(HttpMethod.Post, "/api/backup/auto/run", null);
        ShowAutoBackup(r ?? await Hub.Client.GetAsync<AutoBackupDto>("/api/backup/auto"));
        BackupStatus = r is null ? $"Niet gelukt: {error}" : $"Reservekopie gemaakt: {r.Files.FirstOrDefault()?.Name}";
    }

    /// <summary>Stores the daily backup settings; returns an error text when the engine refused them.</summary>
    private async Task<string?> SaveAutoBackupAsync()
    {
        if (!AutoBackupLoaded) return null;
        var (r, error) = await Hub.Client.SendWithErrorAsync<AutoBackupDto>(HttpMethod.Put, "/api/backup/auto",
            new AutoBackupSettingsDto(AutoBackupEnabled, AutoBackupFolder.Trim(), AutoBackupTime.Trim(), (int)AutoBackupKeep));
        if (r is not null) ShowAutoBackup(r);
        return r is null ? $"Dagelijkse reservekopie niet opgeslagen: {error}" : null;
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
        if (_softLoaded) await Hub.Client.PutAsync("/api/soft-start", new SoftStartWire((double)SoftMaxWait));

        if (AudioLoaded)
        {
            var a = await Hub.Client.SendForAsync<AudioSettingsDto>(HttpMethod.Put, "/api/audio",
                new AudioSettingsRequest(ProgramAsio?.Id ?? "", PreviewAsio?.Id ?? "", SampleRate, (int)BufferMs, (int)AsioBuffer));
            if (a is not null) ShowAudio(a);
        }

        if (SpotifyLoaded && SpotifyChanged) await SaveSpotifyAsync();
        if (AcoustIdLoaded && AcoustIdKey.Trim().Length > 0) await SaveAcoustIdAsync();
        StoreSoloist();

        string? failoverError = await Failover.SaveAsync();

        if (SystemLoaded)
        {
            var sys = await Hub.Client.SendForAsync<SystemDto>(HttpMethod.Put, "/api/system", new SystemSettingsRequest(AutoStart, DetailedLog));
            if (sys is not null) { AutoStart = sys.AutoStart; AutoStartCommand = sys.AutoStartCommand ?? ""; }
        }
        string? autoBackupError = await SaveAutoBackupAsync();

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
        if (failoverError is not null) notes.Add(failoverError + ".");
        if (autoBackupError is not null) notes.Add(autoBackupError);
        Status = string.Join(" ", notes);
    }

    // ---------------------------------------------------------------- Spotify

    private bool SpotifyChanged => _spotify is null || SpotifySecret.Trim().Length > 0
        || SpotifyClientId.Trim() != _spotify.ClientId || !string.Equals(SpotifyMarket.Trim(), _spotify.Market, StringComparison.OrdinalIgnoreCase);

    private async Task LoadSpotifyAsync()
    {
        var s = await Hub.Client.GetAsync<SpotifySettingsDto>("/api/spotify/settings");
        SpotifyLoaded = s is not null;
        if (s is not null) ShowSpotify(s);
    }

    private void ShowSpotify(SpotifySettingsDto s)
    {
        _spotify = s;
        SpotifyClientId = s.ClientId;
        SpotifyMarket = s.Market;
        SpotifySecret = "";
        SpotifySecretHint = s.SecretSet
            ? $"Er is een Client Secret opgeslagen ({s.SecretHint}); leeg laten = houden."
            : "Nog geen Client Secret opgeslagen.";
    }

    private void SpotifyResult(bool ok, string text)
    {
        SpotifyOk = ok;
        SpotifyFailed = !ok;
        SpotifyStatus = text;
    }

    private async Task<bool> SaveSpotifyAsync()
    {
        var (s, error) = await Hub.Client.SendWithErrorAsync<SpotifySettingsDto>(HttpMethod.Put, "/api/spotify/settings",
            new SpotifySettingsRequest(SpotifyClientId.Trim(), SpotifySecret.Trim(), SpotifyMarket.Trim()));
        if (s is null) { SpotifyResult(false, error ?? "Opslaan mislukt."); return false; }
        ShowSpotify(s);
        return true;
    }

    /// <summary>Saves the keys when they changed, then lets the engine sign in at Spotify with them.</summary>
    [RelayCommand]
    private async Task TestSpotify()
    {
        if (SpotifyBusy) return;
        SpotifyBusy = true;
        SpotifyResult(true, "verbinden met Spotify…");
        SpotifyFailed = SpotifyOk = false;
        try
        {
            if (SpotifyChanged && !await SaveSpotifyAsync()) return;
            var r = await Hub.Client.SendForAsync<SpotifyTestDto>(HttpMethod.Post, "/api/spotify/test", null);
            if (r is null) SpotifyResult(false, "De engine gaf geen antwoord.");
            else SpotifyResult(r.Ok, r.Message);
        }
        finally { SpotifyBusy = false; }
    }

    [RelayCommand]
    private async Task ClearSpotify()
    {
        var s = await Hub.Client.SendForAsync<SpotifySettingsDto>(HttpMethod.Put, "/api/spotify/settings",
            new SpotifySettingsRequest(null, null, null, Clear: true));
        if (s is null) return;
        ShowSpotify(s);
        SpotifyResult(true, "De Spotify-gegevens zijn gewist.");
    }

    /// <summary>
    /// The Web Playback test page on the engine. Spotify only accepts a loopback IP address as a redirect, so
    /// "localhost" becomes 127.0.0.1; on an engine elsewhere in the network the page cannot be used from here.
    /// </summary>
    public string SpotifyPlayerUrl
    {
        get
        {
            if (!Uri.TryCreate(Hub.Settings.EffectiveEngineUrl, UriKind.Absolute, out var u)) return "";
            string host = u.Host is "localhost" or "::1" or "[::1]" ? "127.0.0.1" : u.Host;
            return $"http://{host}:{u.Port}/dev/spotify-player.html";
        }
    }

    public bool SpotifyPlayerLocal => Uri.TryCreate(Hub.Settings.EffectiveEngineUrl, UriKind.Absolute, out var u) && u.IsLoopback;

    [RelayCommand]
    private void OpenSpotifyPlayer()
    {
        if (!SpotifyPlayerLocal) { SpotifyResult(false, "De testpagina werkt alleen op de computer waar de engine draait."); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SpotifyPlayerUrl) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    // ---------------------------------------------------------------- AcoustID

    private async Task LoadAcoustIdAsync()
    {
        var s = await Hub.Client.GetAsync<AcoustIdSettingsDto>("/api/acoustid/settings");
        AcoustIdLoaded = s is not null;
        if (s is not null) ShowAcoustId(s);
    }

    private void ShowAcoustId(AcoustIdSettingsDto s)
    {
        AcoustIdKey = "";
        AcoustIdKeyHint = s.KeySet ? $"Er is een sleutel opgeslagen ({s.KeyHint}). Leeg laten = die houden." : "Nog geen sleutel opgeslagen.";
        AcoustIdFpcalc = s.FpcalcFound ? "fpcalc (Chromaprint) is gevonden bij de engine."
            : "fpcalc ontbreekt bij de engine: voer scripts\\fetch-fpcalc.ps1 uit en bouw de engine opnieuw.";
        if (s.RegisterUrl.StartsWith("https://", StringComparison.Ordinal)) _acoustIdRegisterUrl = s.RegisterUrl;
    }

    private void AcoustIdResult(bool ok, string text)
    {
        AcoustIdOk = ok;
        AcoustIdFailed = !ok;
        AcoustIdStatus = text;
    }

    private async Task<bool> SaveAcoustIdAsync()
    {
        var (s, error) = await Hub.Client.SendWithErrorAsync<AcoustIdSettingsDto>(HttpMethod.Put, "/api/acoustid/settings",
            new AcoustIdSettingsRequest(AcoustIdKey.Trim()));
        if (s is null) { AcoustIdResult(false, error ?? "Opslaan mislukt."); return false; }
        ShowAcoustId(s);
        return true;
    }

    /// <summary>Saves a new key, then lets the engine check it at AcoustID.</summary>
    [RelayCommand]
    private async Task TestAcoustId()
    {
        if (AcoustIdBusy) return;
        AcoustIdBusy = true;
        AcoustIdOk = AcoustIdFailed = false;
        AcoustIdStatus = "verbinden met AcoustID…";
        try
        {
            if (AcoustIdKey.Trim().Length > 0 && !await SaveAcoustIdAsync()) return;
            var r = await Hub.Client.SendForAsync<AcoustIdTestDto>(HttpMethod.Post, "/api/acoustid/test", null);
            if (r is null) AcoustIdResult(false, "De engine gaf geen antwoord.");
            else AcoustIdResult(r.Ok, r.Message);
        }
        finally { AcoustIdBusy = false; }
    }

    [RelayCommand]
    private async Task ClearAcoustId()
    {
        var s = await Hub.Client.SendForAsync<AcoustIdSettingsDto>(HttpMethod.Put, "/api/acoustid/settings", new AcoustIdSettingsRequest(null, Clear: true));
        if (s is null) return;
        ShowAcoustId(s);
        AcoustIdResult(true, "De AcoustID-sleutel is gewist.");
    }

    /// <summary>The page on acoustid.org where an application is registered (sign in, then "Register a new application").</summary>
    [RelayCommand]
    private void OpenAcoustIdRegister()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_acoustIdRegisterUrl) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [RelayCommand]
    private static void OpenAcoustIdApplications()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://acoustid.org/my-applications") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    // ---------------------------------------------------------------- Spotify Soloist (this computer)

    private bool _soloistWatching;

    private void LoadSoloist()
    {
        var s = Hub.Settings.Soloist;
        SoloistPath = s.ExePath;
        SoloistDevice = s.DeviceName;
        SoloistPort = s.Port;
        SoloistKey = "";
        SoloistStartWithStudio = s.StartWithStudio;
        SoloistAutoConnect = s.AutoConnect;
        if (!_soloistWatching)
        {
            _soloistWatching = true;
            SoloistLauncher.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(ShowSoloist);
        }
        ShowSoloist();
    }

    private void ShowSoloist()
    {
        SoloistKeyHint = "Sleutel: " + SoloistLauncher.KeySource(Hub.Settings.Soloist) + ".";
        SoloistRunning = SoloistLauncher.Running;
        var probe = new SoloistSettings { ExePath = SoloistPath };
        SoloistFound = SoloistLauncher.FindExe(probe) is { } exe
            ? "Gevonden: " + exe
            : SoloistPath.Trim().Length > 0 ? "Op het eigen pad staat geen soloist.exe."
            : "Nog niet gevonden. Gezocht in: " + string.Join("; ", SoloistLauncher.SearchFolders()) + ".";
        SoloistStatus = SoloistLauncher.LastMessage.Length > 0 ? SoloistLauncher.LastMessage
            : SoloistRunning ? "Soloist draait." : "Soloist is niet door de studio gestart.";
    }

    /// <summary>Puts the fields into the studio settings (a new key is encrypted; an empty field keeps the stored key).</summary>
    private void StoreSoloist()
    {
        var s = Hub.Settings.Soloist;
        s.ExePath = SoloistPath.Trim().Trim('"');
        s.DeviceName = string.IsNullOrWhiteSpace(SoloistDevice) ? "REA ontwikkeltest" : SoloistDevice.Trim();
        s.Port = (int)Math.Clamp(SoloistPort, 1024, 65535);
        if (SoloistKey.Trim().Length > 0) s.ApiKeyProtected = SoloistLauncher.Protect(SoloistKey.Trim());
        s.StartWithStudio = SoloistStartWithStudio;
        s.AutoConnect = SoloistAutoConnect;
        Hub.Settings.Save();
        SoloistKey = "";
        ShowSoloist();
    }

    [RelayCommand]
    private void StartSoloist()
    {
        StoreSoloist();
        if (SoloistLauncher.Running) SoloistLauncher.Stop();
        if (SoloistLauncher.Start(Hub.Settings.Soloist) is { } problem) { SoloistStatus = problem; return; }
        ShowSoloist();
    }

    [RelayCommand]
    private void StopSoloist()
    {
        SoloistLauncher.Stop();
        ShowSoloist();
    }

    /// <summary>Unpacks a Soloist zip the user downloaded into the Soloist folder of REA-Radio-DJ.</summary>
    public void InstallSoloistZip(string zipPath)
    {
        SoloistStatus = "installeren…";
        var problem = SoloistLauncher.InstallZip(zipPath);
        if (problem is not null && problem != "restart") { SoloistStatus = problem; return; }
        if (SoloistPath.Trim().Length > 0) SoloistPath = ""; // the REA-Radio-DJ folder takes over from an own path
        StoreSoloist();
        if (problem == "restart") StartSoloist();
    }

    [RelayCommand]
    private static void OpenSoloistFolder()
    {
        Directory.CreateDirectory(SoloistLauncher.ManagedFolder);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SoloistLauncher.ManagedFolder) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [RelayCommand]
    private static void OpenSoloistDownloads()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SoloistTool.DownloadsUrl) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [RelayCommand]
    private void ClearSoloistKey()
    {
        Hub.Settings.Soloist.ApiKeyProtected = "";
        Hub.Settings.Save();
        SoloistKey = "";
        ShowSoloist();
        SoloistStatus = "De opgeslagen Soloist-sleutel is gewist.";
    }

    [RelayCommand]
    private static void OpenSoloistLog()
    {
        if (!File.Exists(SoloistLauncher.LogPath)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SoloistLauncher.LogPath) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    [RelayCommand]
    private static void OpenSpotifyDashboard()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.spotify.com/dashboard") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
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

/// <summary>One play limit in the settings: this kind or category plays at most so long, short ones play out.</summary>
public sealed partial class PlayLimitRow : ObservableObject
{
    [ObservableProperty] private KeyChoice? _side;
    [ObservableProperty] private decimal _maxSeconds;
    [ObservableProperty] private decimal _noFadeUnder;
    [ObservableProperty] private bool _enabled = true;
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

/// <summary>A colour for the accent or the waveform; an empty Hex means "the style's own".</summary>
public sealed record LookColorChoice(string Name, string Hex, Avalonia.Media.IBrush Brush);
