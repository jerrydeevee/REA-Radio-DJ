using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReaRadioDj.Contracts;

namespace ReaRadioDj.Studio.ViewModels;

/// <summary>
/// The presenter voice: status of the GPU and the Higgs TTS 3 server, voice profiles (Higgs with an optional cloned
/// reference voice and control tags, or a Windows voice) and a test console that renders a sentence and plays it on PFL.
/// </summary>
public sealed partial class VoiceTool : StudioTool
{
    public static readonly string[] EngineNames = ["Higgs TTS 3 (GPU)", "Windows-spraak", "ElevenLabs (online)", "Azure (online)"];
    private static readonly string[] EngineKeys = ["higgs", "sapi", "elevenlabs", "azure"];
    /// <summary>Quick tags for Higgs; the text field accepts any tag the server understands.</summary>
    public static readonly string[] QuickTags =
    [
        "<|emotion:enthusiasm|>", "<|emotion:elation|>", "<|emotion:contentment|>", "<|emotion:determination|>",
        "<|emotion:affection|>", "<|emotion:amusement|>", "<|prosody:speed_fast|>", "<|prosody:speed_slow|>",
        "<|prosody:expressive_high|>", "<|prosody:pitch_low|>", "<|style:whispering|>",
    ];

    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _recClock;

    public VoiceTool() : base("voice", "Stemmen")
    {
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await RefreshStatusAsync());
        _recClock = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
            OwnStatus = $"● Opname loopt · {(DateTime.Now - _recStarted).TotalSeconds:0.0} s (lees de tekst rustig voor en druk dan Stop)");
        _recClock.Stop();   // this constructor starts the timer; it only runs while recording
    }

    /// <summary>May this voice speak in the role (presenter, news, weather)? Empty uses = every role.</summary>
    public static bool Allows(VoiceProfileDto p, string role) =>
        string.IsNullOrWhiteSpace(p.Uses) || p.Uses.Split(',', StringSplitOptions.TrimEntries).Contains(role);

    // ---------------------------------------------------------------- roles: presenter, news reader, weather
    public ObservableCollection<VoiceProfileDto> PresenterProfiles { get; } = [];
    public ObservableCollection<Choice> NewsChoices { get; } = [];
    public ObservableCollection<Choice> WeatherChoices { get; } = [];
    [ObservableProperty] private Choice? _newsVoice;
    [ObservableProperty] private Choice? _weatherVoice;
    private bool _settingRoles;

    partial void OnNewsVoiceChanged(Choice? value) { if (!_settingRoles && value is not null) _ = SaveRolesAsync(); }
    partial void OnWeatherVoiceChanged(Choice? value) { if (!_settingRoles && value is not null) _ = SaveRolesAsync(); }

    private async Task SaveRolesAsync()
    {
        var (_, error) = await Hub.Client.SendWithErrorAsync<VoiceRolesDto>(HttpMethod.Put, "/api/voice/roles",
            new VoiceRolesDto(null, NewsVoice?.Id, WeatherVoice?.Id));
        Hub.Status = error is not null ? $"stemrol niet opgeslagen: {error}"
            : $"nieuws: {(NewsVoice?.Id is null ? "presentatorstem" : NewsVoice.Name)} · weer: {(WeatherVoice?.Id is null ? "presentatorstem" : WeatherVoice.Name)}";
        if (error is not null) await ReloadAsync();
    }

    private void FillRoles(IReadOnlyList<VoiceProfileDto> list, VoiceRolesDto? roles)
    {
        _settingRoles = true;
        PresenterProfiles.Clear();
        foreach (var p in list.Where(p => Allows(p, "presenter"))) PresenterProfiles.Add(p);
        Fill(NewsChoices, "news");
        Fill(WeatherChoices, "weather");
        NewsVoice = NewsChoices.FirstOrDefault(c => c.Id == roles?.News) ?? NewsChoices[0];
        WeatherVoice = WeatherChoices.FirstOrDefault(c => c.Id == roles?.Weather) ?? WeatherChoices[0];
        _settingRoles = false;

        void Fill(ObservableCollection<Choice> target, string role)
        {
            target.Clear();
            target.Add(new Choice(null, "(dezelfde stem als de presentator)"));
            foreach (var p in list.Where(p => Allows(p, role))) target.Add(new Choice(p.Id, p.Name ?? ""));
        }
    }

    public IReadOnlyList<string> Engines => EngineNames;
    public IReadOnlyList<string> Tags => QuickTags;
    public ObservableCollection<VoiceProfileDto> Profiles { get; } = [];
    public ObservableCollection<string> SapiVoices { get; } = [];
    public ObservableCollection<string> Results { get; } = [];
    public ObservableCollection<string> ServerLog { get; } = [];

    // status
    [ObservableProperty] private string _gpuName = "geen NVIDIA-GPU gevonden";
    [ObservableProperty] private string _gpuDetail = "";
    [ObservableProperty] private double _gpuMemory;
    [ObservableProperty] private double _gpuLoad;
    [ObservableProperty] private bool _serverOnline;
    [ObservableProperty] private string _serverText = "Higgs-server: controleren…";
    [ObservableProperty] private string _serverDetail = "";
    [ObservableProperty] private bool _serverManaged;
    [ObservableProperty] private bool _serverRunning;
    [ObservableProperty] private string _activeVoice = "";
    [ObservableProperty] private bool _showLog;

    // editor
    [ObservableProperty] private VoiceProfileDto? _selected;
    /// <summary>The voice the automation speaks with (the default profile); choosing another one sets it at once.</summary>
    [ObservableProperty] private VoiceProfileDto? _presenterVoice;
    [ObservableProperty] private string _presenterVoiceNote = "";
    private bool _settingPresenter;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private int _engine;
    [ObservableProperty] private string _referenceAudio = "";
    [ObservableProperty] private string _referenceText = "";
    [ObservableProperty] private string _profileTags = "";
    [ObservableProperty] private double _temperature = 0.8;
    [ObservableProperty] private string? _sapiVoice;
    [ObservableProperty] private bool _consent;
    [ObservableProperty] private string _consentNote = "";
    [ObservableProperty] private string _cloudVoice = "";
    [ObservableProperty] private Choice? _fallback;
    public ObservableCollection<Choice> FallbackChoices { get; } = [];
    [ObservableProperty] private bool _usePresenter = true;
    [ObservableProperty] private bool _useNews = true;
    [ObservableProperty] private bool _useWeather = true;
    [ObservableProperty] private string _owner = "";
    [ObservableProperty] private string _clipResult = "";
    public bool IsOwned => Owner.Length > 0;
    public string OwnerNote => IsOwned
        ? $"Dit is de eigen stem van {Owner}. Alleen {Owner} kan de opname en de toestemming wijzigen of intrekken (onder 'Mijn eigen stem')."
        : "";
    partial void OnOwnerChanged(string value) { OnPropertyChanged(nameof(IsOwned)); OnPropertyChanged(nameof(OwnerNote)); }

    private string UsesText => UsePresenter && UseNews && UseWeather ? ""
        : string.Join(",", new[] { UsePresenter ? "presenter" : null, UseNews ? "news" : null, UseWeather ? "weather" : null }.Where(x => x is not null));

    private void SetUses(string? uses)
    {
        UsePresenter = string.IsNullOrWhiteSpace(uses) || uses.Contains("presenter", StringComparison.Ordinal);
        UseNews = string.IsNullOrWhiteSpace(uses) || uses.Contains("news", StringComparison.Ordinal);
        UseWeather = string.IsNullOrWhiteSpace(uses) || uses.Contains("weather", StringComparison.Ordinal);
    }

    // online voices and the speech check
    [ObservableProperty] private string _elevenLabsKey = "";
    [ObservableProperty] private string _elevenLabsModel = "eleven_multilingual_v2";
    [ObservableProperty] private string _azureKey = "";
    [ObservableProperty] private string _azureRegion = "westeurope";
    [ObservableProperty] private bool _checkEnabled;
    [ObservableProperty] private string _checkUrl = "http://localhost:8002";
    [ObservableProperty] private string _checkModel = "small";
    [ObservableProperty] private decimal? _checkMinScore = 0.75m;
    [ObservableProperty] private decimal? _checkRetries = 1;
    [ObservableProperty] private bool _checkReject = true;
    [ObservableProperty] private string _checkResult = "";

    // test console
    [ObservableProperty] private string _testText = "Goedemiddag! Je luistert naar REA-Radio-DJ. Straks hoor je meer muziek, maar eerst het weer: zonnig en zeventien graden.";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _lastResult = "";

    public bool IsHiggs => Engine == 0;
    public bool IsSapi => Engine == 1;
    public bool IsCloud => Engine >= 2;
    public string CloudVoiceHint => Engine == 2
        ? "stem-ID uit je ElevenLabs-account (Voices → ID)"
        : "naam van de Azure-stem, bijv. nl-NL-FennaNeural, nl-NL-ColetteNeural of nl-NL-MaartenNeural";
    public bool IsClone => IsHiggs && !string.IsNullOrWhiteSpace(ReferenceAudio);
    public string TemperatureText => $"{Temperature:0.00}";
    public string ServerButtonText => ServerRunning ? "Server stoppen" : "Server starten";

    partial void OnEngineChanged(int value)
    {
        OnPropertyChanged(nameof(IsHiggs)); OnPropertyChanged(nameof(IsSapi)); OnPropertyChanged(nameof(IsClone));
        OnPropertyChanged(nameof(IsCloud)); OnPropertyChanged(nameof(CloudVoiceHint));
    }
    partial void OnReferenceAudioChanged(string value) => OnPropertyChanged(nameof(IsClone));
    partial void OnTemperatureChanged(double value) => OnPropertyChanged(nameof(TemperatureText));
    partial void OnServerRunningChanged(bool value) => OnPropertyChanged(nameof(ServerButtonText));

    partial void OnSelectedChanged(VoiceProfileDto? value)
    {
        if (value is not { } p) return;
        Name = p.Name ?? "";
        Engine = Math.Max(0, Array.IndexOf(EngineKeys, p.Engine ?? "higgs"));
        CloudVoice = Engine >= 2 ? p.SapiVoice ?? "" : "";
        FillFallbacks(p.Id);
        Fallback = FallbackChoices.FirstOrDefault(c => c.Id == p.FallbackId) ?? FallbackChoices.FirstOrDefault();
        ReferenceAudio = p.ReferenceAudio ?? "";
        ReferenceText = p.ReferenceText ?? "";
        ProfileTags = p.Tags ?? "";
        Temperature = p.Temperature;
        SapiVoice = SapiVoices.FirstOrDefault(v => !string.IsNullOrEmpty(p.SapiVoice) && v.StartsWith(p.SapiVoice, StringComparison.OrdinalIgnoreCase));
        Consent = p.Consent;
        ConsentNote = p.ConsentNote ?? "";
        Owner = p.Owner ?? "";
        SetUses(p.Uses);
        ClipResult = "";
    }

    private void FillFallbacks(long selfId)
    {
        FallbackChoices.Clear();
        FallbackChoices.Add(new Choice(null, "(geen: alleen Windows-spraak als laatste redmiddel)"));
        foreach (var p in Profiles.Where(p => p.Id != selfId)) FallbackChoices.Add(new Choice(p.Id, p.Name ?? ""));
    }

    protected override void OnFirstShow()
    {
        _ = LoadServicesAsync();
        _ = ReloadAsync();
        _ = LoadInputsAsync();
        _ = OwnRefresh();
        _poll.Start();
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await RefreshStatusAsync();
        var list = await Hub.Client.GetAsync<List<VoiceProfileDto>>("/api/voice/profiles");
        if (list is null) return;
        var keep = Selected?.Id;
        Profiles.Clear();
        foreach (var p in list) Profiles.Add(p);
        Selected = Profiles.FirstOrDefault(p => p.Id == keep) ?? Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles.FirstOrDefault();
        FillRoles(list, await Hub.Client.GetAsync<VoiceRolesDto>("/api/voice/roles"));
        _settingPresenter = true;
        PresenterVoice = PresenterProfiles.FirstOrDefault(p => p.IsDefault) ?? PresenterProfiles.FirstOrDefault();
        _settingPresenter = false;
        PresenterVoiceNote = PresenterVoice is { } d ? Describe(d) : "Nog geen stem: voeg de standaardstemmen toe.";
    }

    private string Describe(VoiceProfileDto p)
    {
        var engine = p.Engine switch { "higgs" => "Higgs (GPU)", "sapi" => "Windows-spraak", "elevenlabs" => "ElevenLabs", "azure" => "Azure", _ => p.Engine };
        var backup = Profiles.FirstOrDefault(x => x.Id == p.FallbackId)?.Name;
        var text = $"De automatisering spreekt AI-breaks in met {p.Name} ({engine})";
        if (backup is not null) text += $"; lukt dat niet, dan met {backup}";
        if (p.Engine == "higgs" && !ServerOnline) text += ". Let op: de Higgs-server is nu offline, dan wordt de reserve gebruikt";
        return text + ". Een persona (venster AI-presentator) en elk nieuws- of weerblok kan ook een eigen stem kiezen.";
    }

    partial void OnPresenterVoiceChanged(VoiceProfileDto? value)
    {
        if (_settingPresenter || value is null || value.IsDefault) return;
        _ = SetPresenterAsync(value);
    }

    private async Task SetPresenterAsync(VoiceProfileDto p)
    {
        if (await Hub.Client.PostAsync($"/api/voice/profiles/{p.Id}/default")) Hub.Status = $"'{p.Name}' is nu de presentatorstem";
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task AddPresets()
    {
        var r = await Hub.Client.SendForAsync<PresetsAddedDto>(HttpMethod.Post, "/api/voice/profiles/presets", null);
        Hub.Status = r is null ? "standaardstemmen toevoegen mislukt" : r.Added == 0 ? "de standaardstemmen staan er al" : $"{r.Added} standaardstemmen toegevoegd";
        await ReloadAsync();
    }

    private async Task RefreshStatusAsync()
    {
        var s = await Hub.Client.GetAsync<VoiceStatusDto>("/api/voice/status");
        if (s is null) return;
        if (s.Gpu is { } g)
        {
            GpuName = g.Name;
            GpuMemory = g.MemoryTotalMb > 0 ? g.MemoryUsedMb / g.MemoryTotalMb * 100 : 0;
            GpuLoad = g.UtilizationPercent;
            GpuDetail = $"{g.MemoryUsedMb / 1024:0.0} / {g.MemoryTotalMb / 1024:0.0} GB · {g.UtilizationPercent:0}% belast · {g.TemperatureC:0} °C · driver {g.Driver}";
        }
        ServerOnline = s.ServerOnline;
        ServerText = s.ServerOnline ? $"Higgs-server online · {s.LatencyMs:0} ms" : "Higgs-server offline";
        ServerDetail = s.ServerOnline
            ? $"{s.BaseUrl} · {(s.Models.Count > 0 ? string.Join(", ", s.Models) : s.Model)}"
            : $"{s.BaseUrl} · {s.ServerError}{(s.FallbackToSapi ? " · reserve: Windows-spraak" : "")}";
        ServerManaged = s.ServerManaged;
        ServerRunning = s.ServerRunning;
        ActiveVoice = s.DefaultVoice is null ? "geen stemprofiel" : $"Presentatorstem: {s.DefaultVoice} ({(s.Engine == "higgs" ? "Higgs" : "Windows")})";
        if (SapiVoices.Count == 0) foreach (var v in s.SapiVoices) SapiVoices.Add(v);
        if (ShowLog)
        {
            var log = await Hub.Client.GetAsync<List<string>>("/api/voice/server/log") ?? [];
            ServerLog.Clear();
            foreach (var l in log.TakeLast(80)) ServerLog.Add(l);
        }
    }

    private VoiceProfileDto Current(long id, bool isDefault) => new(id, Name, EngineKeys[Math.Clamp(Engine, 0, EngineKeys.Length - 1)],
        ReferenceAudio, ReferenceText, ProfileTags, Math.Round(Temperature, 2),
        IsCloud ? CloudVoice.Trim() : SapiVoice is { } v ? v.Split(" (")[0] : "", Consent, ConsentNote, isDefault, null, Fallback?.Id,
        Owner, UsesText);

    [RelayCommand]
    private void NewCloud()
    {
        Selected = null;
        Owner = ""; SetUses(null);
        Name = "Online stem (Azure)"; Engine = 3; CloudVoice = "nl-NL-FennaNeural"; ProfileTags = ""; ReferenceAudio = ""; ReferenceText = "";
        Consent = false; ConsentNote = "";
        FillFallbacks(0);
        Fallback = FallbackChoices.FirstOrDefault();
    }

    // ---------------------------------------------------------------- online voices and speech check

    private async Task LoadServicesAsync()
    {
        if (await Hub.Client.GetAsync<VoiceServicesDto>("/api/voice/services") is not { } d) return;
        ElevenLabsKey = d.ElevenLabsKey;
        ElevenLabsModel = d.ElevenLabsModel;
        AzureKey = d.AzureKey;
        AzureRegion = d.AzureRegion;
        CheckEnabled = d.CheckEnabled;
        CheckUrl = d.CheckUrl;
        CheckModel = d.CheckModel;
        CheckMinScore = (decimal)d.CheckMinScore;
        CheckRetries = d.CheckRetries;
        CheckReject = d.CheckReject;
    }

    [RelayCommand]
    private async Task SaveServices()
    {
        var d = new VoiceServicesDto(ElevenLabsKey, ElevenLabsModel, AzureKey, AzureRegion, CheckEnabled, CheckUrl, CheckModel,
            (double)(CheckMinScore ?? 0.75m), (int)(CheckRetries ?? 1), CheckReject);
        if (await Hub.Client.SendForAsync<VoiceServicesDto>(HttpMethod.Put, "/api/voice/services", d) is { } saved)
        {
            ElevenLabsKey = saved.ElevenLabsKey;
            AzureKey = saved.AzureKey;
            Hub.Status = "online stemmen en controle opgeslagen";
        }
    }

    /// <summary>Speaks the test text with the presenter voice and lets Whisper listen back.</summary>
    [RelayCommand]
    private async Task CheckTest()
    {
        if (Busy) return;
        Busy = true;
        CheckResult = "inspreken en terugluisteren…";
        try
        {
            await SaveServices();
            var r = await Hub.Client.SendForAsync<CheckTestDto>(HttpMethod.Post, "/api/voice/check-test", new CheckTestRequest(TestText));
            CheckResult = r is null ? "geen antwoord"
                : r.Ok ? $"score {r.Score:0.00} ({r.Voice}) · gehoord: \"{r.Heard}\"" : $"✕ {r.Error}";
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private void NewHiggs()
    {
        Selected = null;
        Owner = ""; SetUses(null);
        Name = "Nieuwe Higgs-stem"; Engine = 0; ReferenceAudio = ""; ReferenceText = ""; ProfileTags = "<|emotion:enthusiasm|>";
        Temperature = 0.8; Consent = false; ConsentNote = "";
    }

    [RelayCommand]
    private void NewSapi()
    {
        Selected = null;
        Owner = ""; SetUses(null);
        Name = "Windows-stem"; Engine = 1; ProfileTags = ""; ReferenceAudio = ""; ReferenceText = ""; Consent = false; ConsentNote = "";
        SapiVoice = SapiVoices.FirstOrDefault(v => v.Contains("nl-", StringComparison.OrdinalIgnoreCase)) ?? SapiVoices.FirstOrDefault();
    }

    [RelayCommand]
    private void AddTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag) || ProfileTags.Contains(tag, StringComparison.Ordinal)) return;
        // one tag per category (emotion, style, speed, pitch, expressiveness): a new one replaces the old one
        string pattern = tag switch
        {
            _ when tag.StartsWith("<|emotion:", StringComparison.Ordinal) => @"<\|emotion:[^|]*\|>",
            _ when tag.StartsWith("<|style:", StringComparison.Ordinal) => @"<\|style:[^|]*\|>",
            _ when tag.Contains(":speed_", StringComparison.Ordinal) => @"<\|prosody:speed_[^|]*\|>",
            _ when tag.Contains(":pitch_", StringComparison.Ordinal) => @"<\|prosody:pitch_[^|]*\|>",
            _ when tag.Contains(":expressive_", StringComparison.Ordinal) => @"<\|prosody:expressive_[^|]*\|>",
            _ => "(?!)",
        };
        ProfileTags = System.Text.RegularExpressions.Regex.Replace(ProfileTags, pattern, "") + tag;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (IsClone && !Consent) { Hub.Status = "gekloonde stem: vink eerst aan dat er schriftelijke toestemming is"; return; }
        var dto = Current(Selected?.Id ?? 0, Selected?.IsDefault ?? Profiles.Count == 0);
        bool ok = Selected is { } s ? await Hub.Client.PutAsync($"/api/voice/profiles/{s.Id}", dto) : await Hub.Client.PostAsync("/api/voice/profiles", dto);
        if (!ok) return;
        Hub.Status = $"stem '{Name}' opgeslagen";
        var keepName = Name;
        await ReloadAsync();
        Selected = Profiles.FirstOrDefault(p => p.Name == keepName) ?? Selected;
    }

    [RelayCommand]
    private async Task MakeDefault()
    {
        if (Selected is not { } s) return;
        if (await Hub.Client.PostAsync($"/api/voice/profiles/{s.Id}/default")) { Hub.Status = $"'{s.Name}' is nu de presentatorstem"; await ReloadAsync(); }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is { } s && await Hub.Client.DeleteAsync($"/api/voice/profiles/{s.Id}")) { Selected = null; await ReloadAsync(); }
    }

    [RelayCommand]
    private async Task Test()
    {
        if (Busy) return;
        Busy = true;
        LastResult = IsHiggs ? "bezig met renderen op de GPU…" : "bezig…";
        try
        {
            // Test what is in the editor: unsaved tags are sent along.
            var r = await Hub.Client.SendForAsync<VoiceTestResultDto>(HttpMethod.Post, "/api/voice/test",
                new VoiceTestRequest(TestText, Selected?.Id, ProfileTags, true));
            if (r is null) { LastResult = "geen antwoord van de engine"; return; }
            LastResult = r.Ok
                ? $"✓ {(r.Engine == "higgs" ? "Higgs" : "Windows")} · {r.AudioSeconds:0.0} s audio in {r.RenderSeconds:0.0} s ({(r.RenderSeconds > 0 ? r.AudioSeconds / r.RenderSeconds : 0):0.0}× realtime) · speelt op PFL"
                : $"✕ {r.Error}";
            Results.Insert(0, $"{DateTime.Now:HH:mm:ss}  {Name}  {LastResult}");
            while (Results.Count > 30) Results.RemoveAt(Results.Count - 1);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task ToggleServer()
    {
        await Hub.Client.PostAsync(ServerRunning ? "/api/voice/server/stop" : "/api/voice/server/start");
        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task ToggleLog()
    {
        ShowLog = !ShowLog;
        await RefreshStatusAsync();
    }

    // ---------------------------------------------------------------- a recording for the voice in the editor

    /// <summary>Sends a chosen WAV file to the engine as the recording of the voice in the editor.</summary>
    public async Task UploadClipAsync(byte[] wav)
    {
        if (Selected is not { } s) { ClipResult = "Sla de stem eerst op en kies daarna de opname."; return; }
        var (r, error) = await Hub.Client.PostBytesAsync<VoiceClipDto>($"/api/voice/profiles/{s.Id}/clip", wav);
        if (error is not null || r is null) { ClipResult = $"✕ {error ?? "geen antwoord"}"; return; }
        var keep = s.Id;
        await ReloadAsync();
        Selected = Profiles.FirstOrDefault(p => p.Id == keep);
        ClipResult = $"✓ Opname opgeslagen ({r.Seconds:0.0} s, piek {r.PeakDb:0} dB). Vul de letterlijke tekst in, geef toestemming en sla op.";
    }

    // ---------------------------------------------------------------- my own voice (a presenter)

    public ObservableCollection<DeviceDto> InputDevices { get; } = [];
    [ObservableProperty] private DeviceDto? _ownDevice;
    [ObservableProperty] private string _ownName = "";
    [ObservableProperty] private string _ownWho = "";
    [ObservableProperty] private bool _ownSignInNeeded;
    [ObservableProperty] private string _ownText = "";
    [ObservableProperty] private bool _ownRecording;
    [ObservableProperty] private string _ownStatus = "";
    [ObservableProperty] private string _ownTake = "";
    [ObservableProperty] private bool _ownHasTake;
    [ObservableProperty] private bool _ownConsent;
    [ObservableProperty] private bool _ownPresenter = true;
    [ObservableProperty] private bool _ownNews;
    [ObservableProperty] private bool _ownWeather;
    [ObservableProperty] private VoiceProfileDto? _ownProfile;
    [ObservableProperty] private bool _ownConfirmRemove;
    private DateTime _recStarted;
    private long? _ownLoadedFor;

    /// <summary>Without users at the station the presenter types a name; with users it is the signed-in name.</summary>
    public bool OwnNeedsName => !Hub.UsersActive;
    public bool HasOwnVoice => OwnProfile is not null;
    public string OwnRecordText => OwnRecording ? "■ Stop" : "● Opnemen";
    public string OwnRemoveText => OwnConfirmRemove ? "Zeker weten? Klik nogmaals" : "Mijn stem verwijderen";
    public string OwnWhoText => OwnSignInNeeded ? "Meld je eerst aan (rechtsboven); je eigen stem wordt aan jouw naam gekoppeld."
        : OwnWho.Length > 0 ? $"Eigen stem van {OwnWho}" : "Vul je naam in zoals je op de radio heet en druk Ophalen.";
    public string OwnVoiceText => OwnProfile is { } p
        ? $"Vastgelegd: {p.Name} ({p.Summary}). {p.ConsentNote}"
        : "Er is nog geen eigen stem vastgelegd.";

    partial void OnOwnRecordingChanged(bool value) => OnPropertyChanged(nameof(OwnRecordText));
    partial void OnOwnConfirmRemoveChanged(bool value) => OnPropertyChanged(nameof(OwnRemoveText));
    partial void OnOwnWhoChanged(string value) => OnPropertyChanged(nameof(OwnWhoText));
    partial void OnOwnSignInNeededChanged(bool value) => OnPropertyChanged(nameof(OwnWhoText));
    partial void OnOwnProfileChanged(VoiceProfileDto? value) { OnPropertyChanged(nameof(HasOwnVoice)); OnPropertyChanged(nameof(OwnVoiceText)); }

    private string OwnerQuery => OwnNeedsName ? "?owner=" + Uri.EscapeDataString(OwnName.Trim()) : "";

    private async Task LoadInputsAsync()
    {
        if (await Hub.Client.GetAsync<List<DeviceDto>>("/api/record-devices") is not { } list) return;
        InputDevices.Clear();
        foreach (var d in list.Where(d => d.Enabled)) InputDevices.Add(d);
        if (await Hub.Client.GetAsync<RoutingDto>("/api/routing") is { } r)
            OwnDevice = InputDevices.FirstOrDefault(d => d.Index == r.MicDevice) ?? InputDevices.FirstOrDefault(d => d.IsDefault) ?? InputDevices.FirstOrDefault();
    }

    private void ApplyOwn(OwnVoiceDto? d, string? error)
    {
        if (error is not null) { _recClock.Stop(); OwnStatus = $"✕ {error}"; return; }
        if (d is null) return;
        OnPropertyChanged(nameof(OwnNeedsName));
        OwnWho = d.Owner ?? "";
        OwnSignInNeeded = d.SignInNeeded;
        OwnProfile = d.Profile;
        if (d.Profile is { } p && _ownLoadedFor != p.Id)
        {
            _ownLoadedFor = p.Id;
            OwnText = string.IsNullOrWhiteSpace(p.ReferenceText) ? d.ReadText : p.ReferenceText;
            OwnPresenter = Allows(p, "presenter");
            OwnNews = Allows(p, "news");
            OwnWeather = Allows(p, "weather");
        }
        if (OwnText.Length == 0) OwnText = d.ReadText;
        OwnRecording = d.Recording;
        if (d.Recording) { _recStarted = DateTime.Now - TimeSpan.FromSeconds(d.RecordingSeconds); _recClock.Start(); }
        else
        {
            _recClock.Stop();
            if (OwnStatus.StartsWith('●')) OwnStatus = "";
        }
        OwnHasTake = d.Take is not null;
        OwnTake = d.Take is not { } t ? ""
            : t.Problem is null ? $"✓ Opname van {t.Seconds:0.0} s, piek {t.PeakDb:0} dB. Beluister hem op PFL en leg hem vast."
            : $"✕ {t.Problem}";
    }

    [RelayCommand]
    private async Task OwnRefresh()
    {
        OnPropertyChanged(nameof(OwnNeedsName));
        // Also without a name: the answer carries the text to read.
        ApplyOwn(await Hub.Client.GetAsync<OwnVoiceDto>("/api/voice/mine" + OwnerQuery), null);
    }

    [RelayCommand]
    private async Task OwnRecord()
    {
        OwnConfirmRemove = false;
        bool stop = OwnRecording;
        if (!stop) OwnStatus = "";
        var (d, error) = await Hub.Client.SendWithErrorAsync<OwnVoiceDto>(HttpMethod.Post,
            stop ? "/api/voice/mine/record/stop" : "/api/voice/mine/record/start", new OwnVoiceRecordRequest(OwnName.Trim(), OwnDevice?.Index));
        ApplyOwn(d, error);
    }

    /// <summary>A WAV file chosen on this computer instead of a recording.</summary>
    public async Task OwnUploadAsync(byte[] wav)
    {
        var (d, error) = await Hub.Client.PostBytesAsync<OwnVoiceDto>("/api/voice/mine/upload" + OwnerQuery, wav);
        ApplyOwn(d, error);
    }

    [RelayCommand]
    private async Task OwnPlay()
    {
        var (d, error) = await Hub.Client.SendWithErrorAsync<OwnVoiceDto>(HttpMethod.Post, "/api/voice/mine/take/play" + OwnerQuery, null);
        ApplyOwn(d, error);
        if (error is null) OwnStatus = "De opname speelt op PFL (niet ON AIR).";
    }

    [RelayCommand]
    private async Task OwnDiscard()
    {
        var (d, error) = await Hub.Client.SendWithErrorAsync<OwnVoiceDto>(HttpMethod.Delete, "/api/voice/mine/take" + OwnerQuery, null);
        ApplyOwn(d, error);
    }

    [RelayCommand]
    private async Task OwnSave()
    {
        OwnConfirmRemove = false;
        if (!OwnConsent) { OwnStatus = "✕ Vink eerst aan dat je toestemming geeft."; return; }
        var uses = string.Join(",", new[] { OwnPresenter ? "presenter" : null, OwnNews ? "news" : null, OwnWeather ? "weather" : null }.Where(x => x is not null));
        var (d, error) = await Hub.Client.SendWithErrorAsync<OwnVoiceDto>(HttpMethod.Post, "/api/voice/mine",
            new OwnVoiceSaveRequest(OwnName.Trim(), OwnText, true, uses));
        _ownLoadedFor = null;
        ApplyOwn(d, error);
        if (error is not null) return;
        OwnConsent = false;
        OwnStatus = "✓ Je stem is vastgelegd. Probeer hem uit met de proefopname onderaan.";
        await ReloadAsync();
        if (Profiles.FirstOrDefault(p => p.Id == d?.Profile?.Id) is { } mine) Selected = mine;
    }

    [RelayCommand]
    private async Task OwnRemove()
    {
        if (!OwnConfirmRemove) { OwnConfirmRemove = true; return; }
        OwnConfirmRemove = false;
        var (d, error) = await Hub.Client.SendWithErrorAsync<OwnVoiceDto>(HttpMethod.Delete, "/api/voice/mine" + OwnerQuery, null);
        ApplyOwn(d, error);
        if (error is not null) return;
        _ownLoadedFor = null;
        OwnText = d?.ReadText ?? "";
        OwnStatus = "Je eigen stem en de opname zijn verwijderd; je toestemming is ingetrokken.";
        await ReloadAsync();
    }
}
