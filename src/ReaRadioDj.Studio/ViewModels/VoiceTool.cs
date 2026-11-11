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

    public VoiceTool() : base("voice", "Stemmen")
    {
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await RefreshStatusAsync());
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
        _settingPresenter = true;
        PresenterVoice = Profiles.FirstOrDefault(p => p.IsDefault) ?? Profiles.FirstOrDefault();
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
        return text + ". Een presentator (venster AI-presentator) kan een eigen stem hebben.";
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
        IsCloud ? CloudVoice.Trim() : SapiVoice is { } v ? v.Split(" (")[0] : "", Consent, ConsentNote, isDefault, null, Fallback?.Id);

    [RelayCommand]
    private void NewCloud()
    {
        Selected = null;
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
        Name = "Nieuwe Higgs-stem"; Engine = 0; ReferenceAudio = ""; ReferenceText = ""; ProfileTags = "<|emotion:enthusiasm|>";
        Temperature = 0.8; Consent = false; ConsentNote = "";
    }

    [RelayCommand]
    private void NewSapi()
    {
        Selected = null;
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
}
